# XafAIReportDesigner

Standalone WinForms AI-powered report designer built on DevExpress XtraReports 26.1. Reports are
generated from natural-language prompts against a Northwind-style PostgreSQL data model — the
primary path is an **own provider-agnostic AI pipeline** (any model via LLMTornado; ~4s per
generation) that handles master-detail correctly; the DevExpress CTP wizard and chat remain
available as secondary paths.

## Features

- **AI Prompt-to-Report wizard** (DevExpress `ReportPromptToReportBehavior`, multi-agent, CTP) —
  create reports from a prompt; the wizard attaches data-source metadata and asks clarification
  questions when the prompt is ambiguous.
- **AI Assistant chat** (`ReportModifyBehavior`, CTP) — edit the open report in natural language:
  add bands/tables, restyle, group/sort/filter. Command-style instructions work; questions don't.
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
- **Report persistence** — Load/Save to the XAF `ReportDataV2` table in PostgreSQL; saved layouts
  store the connection *name only* and credentials are restored on load.
- Full DevExpress Report Designer ribbon UI.

## Prerequisites

- .NET 10.0 SDK
- PostgreSQL (a `postgres:17` container works; seed with `seed-postgres.sql`)
- OpenAI API key. The own pipeline works with any capable model (default `gpt-5.4-mini`).
  The DevExpress CTP wizard/chat still require `gpt-5.2` — the only model that reliably
  completes the DX multi-agent workflow.
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
       "Model": "gpt-5.2",
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
- The DX CTP paths keep their old caveats: 2–5 min per generation on gpt-5.2 only; the Modify
  chat executes layout *commands* only (questions fail with a JSON parse error).
- Keep prompts free of data semantics that contradict the schema (e.g. discount formulas) —
  newer models refuse on contradictions, older ones silently pick a side.

## Tech Stack

- .NET 10.0 (`net10.0` / `net10.0-windows`, ReportDesigner uses the Razor SDK — the AI chat
  panel is a Blazor WebView)
- DevExpress XtraReports + AI Integration 26.1.3
- EF Core 8.0.18 + PostgreSQL (Npgsql 8)
- LLMTornado + Microsoft.Extensions.AI 10.x — the own pipeline talks to any provider through
  `IChatClient`; OpenAI SDK remains for the DX CTP behaviors
