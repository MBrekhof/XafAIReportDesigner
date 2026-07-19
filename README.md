# XafAIReportDesigner

Standalone WinForms AI-powered report designer built on DevExpress XtraReports 26.1. Reports are
generated and modified from natural-language prompts against a Northwind-style PostgreSQL data
model through an **own provider-agnostic AI pipeline** (any model via LLMTornado; ~4s per
generation, ~2s per modification) that handles master-detail correctly. The DevExpress AI CTP
integration was evaluated first and abandoned (slow, gpt-5.2-only, unreliable chat — full story
in DOCS/DONE.md).

## Features

- **Own AI pipeline** (Database → AI → *Generate from Prompt*, with a model dropdown) — the LLM
  fills a small report-spec JSON; the deterministic `ReportSpecTranslator` builds the XtraReport.
  Provider-agnostic via LLMTornado — proven on gpt-5.4-mini (a model the DX CTP workflow cannot
  use) at ~4s per generation vs 2–5 min for the CTP pipeline. Includes:
  - `ReflectionSchemaDiscoveryService` — discovers `[AIVisible]`/`[AIDescription]` entities and
    emits the schema text (tables, columns, enums, FK graph) for the spec prompt;
  - `SchemaSqlDataSourceFactory` — builds the matching `SqlDataSource` (query per table +
    named master-detail relations both FK directions) and validates every generated expression
    path against the schema/relation graph;
  - `ReportSpecTranslator` — spec→layout translation encoding the band shapes proven against
    DX-generated layouts, plus deterministic chain repair (BFS over the relation graph fixes
    under-/over-qualified and wrong-direction field paths the LLM emits);
  - **validation + cheap retries** — up to 3 fresh rolls, best result wins; remaining issues
    are shown as a warning.
- **Modify via AI** (same ribbon group) — spec-level modification for own-pipeline reports: the
  spec JSON travels inside the layout (`XtraReport.Extensions`), the LLM edits the spec, the
  translator rebuilds. Structural edits ("move quantity to the first column") are array edits —
  reliable by construction, ~2-3s. Replaces the DX CTP Modify chat for these reports.
- **Report persistence** — Load/Save to the XAF `ReportDataV2` table in PostgreSQL; saved layouts
  store the connection *name only* and credentials are restored on load.
- Full DevExpress Report Designer ribbon UI.

## Prerequisites

- .NET 10.0 SDK
- PostgreSQL (a `postgres:17` container works; seed with `seed-postgres.sql`)
- OpenAI API key. Any capable model works (default `gpt-5.4-mini`; pick per generation via
  the ribbon's Model dropdown).
- DevExpress 26.1 license (local NuGet feed)

## Quick Start

1. Create the database and seed it:
   ```bash
   docker run -d --name xaf-postgres -p 5432:5432 -e POSTGRES_USER=xaf -e POSTGRES_PASSWORD=xaf123 postgres:17
   docker exec xaf-postgres psql -U xaf -d postgres -c "CREATE DATABASE xafaireportdesigner OWNER xaf;"
   # then pipe seed-postgres.sql into psql -d xafaireportdesigner
   ```
2. Create `XafAIReportDesigner/XafAIReportDesigner.ReportDesigner/appsettings.Development.json`:
   ```json
   {
     "OpenAI": {
       "ApiKey": "sk-your-key-here",
       "GenerateModel": "gpt-5.4-mini"
     },
     "Database": {
       "ConnectionString": "Host=localhost;Port=5432;Database=xafaireportdesigner;Username=xaf;Password=xaf123",
       "XpoConnectionString": "XpoProvider=Postgres;Server=localhost;Port=5432;User ID=xaf;Password=xaf123;Database=xafaireportdesigner;Encoding=UNICODE"
     }
   }
   ```
3. Build and run:
   ```bash
   dotnet build XafAIReportDesigner.slnx
   dotnet run --project XafAIReportDesigner/XafAIReportDesigner.ReportDesigner
   ```

## Known limitations

- The own pipeline's spec covers title/master fields/nested levels/columns/totals — the common
  report shapes. Exotic layouts (cross-tabs, charts, side-by-side subreports) are not in the
  spec yet; extend `ReportSpec` + `ReportSpecTranslator` as needs appear.
- Keep prompts free of data semantics that contradict the schema (e.g. discount formulas) —
  newer models refuse on contradictions, older ones silently pick a side.

## Tech Stack

- .NET 10.0 (`net10.0` / `net10.0-windows`)
- DevExpress XtraReports 26.1.3
- EF Core 8.0.18 + PostgreSQL (Npgsql 8)
- LLMTornado + Microsoft.Extensions.AI — the pipeline talks to any provider through
  `IChatClient`
