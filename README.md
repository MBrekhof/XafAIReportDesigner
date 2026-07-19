# XafAIReportDesigner

Standalone WinForms AI-powered report designer built on DevExpress XtraReports 26.1 and its AI
Integration. Reports are generated from natural-language prompts against a Northwind-style
PostgreSQL data model — through the built-in AI wizard, an AI chat that edits open layouts, and a
schema-aware headless generation pipeline that handles master-detail correctly.

## Features

- **AI Prompt-to-Report wizard** (DevExpress `ReportPromptToReportBehavior`, multi-agent, CTP) —
  create reports from a prompt; the wizard attaches data-source metadata and asks clarification
  questions when the prompt is ambiguous.
- **AI Assistant chat** (`ReportModifyBehavior`, CTP) — edit the open report in natural language:
  add bands/tables, restyle, group/sort/filter. Command-style instructions work; questions don't.
- **Headless generation** (Database → AI → *Generate from Prompt*) — the 26.1 cross-platform API
  (`GeneratePromptToReportAsync`) fed with a curated schema **including foreign-key relationships**,
  which the wizard never provides. This is the path that gets master-detail reports (invoice +
  items) right. Includes:
  - `ReflectionSchemaDiscoveryService` — discovers `[AIVisible]`/`[AIDescription]` entities and
    emits the schema text plus binding rules for the AI;
  - `SchemaSqlDataSourceFactory` — builds the matching `SqlDataSource` (query per table +
    named master-detail relations both FK directions) and attaches it to generated reports;
  - **binding validation + retry** — every generated expression path is resolved against the
    schema/relation graph; on failures one fresh generation runs and the better result wins,
    with remaining issues shown as a warning.
- **Report persistence** — Load/Save to the XAF `ReportDataV2` table in PostgreSQL; saved layouts
  store the connection *name only* and credentials are restored on load.
- Full DevExpress Report Designer ribbon UI.

## Prerequisites

- .NET 10.0 SDK
- PostgreSQL (a `postgres:17` container works; seed with `seed-postgres.sql`)
- OpenAI API key — **use `gpt-5.2`**: it is currently the only model that reliably completes the
  DevExpress multi-agent generation workflow (faster tiers produce broken structures or refuse;
  see TODO/DONE notes). Older models (gpt-4o) fail with JSON parse errors and weak layouts.
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
       "Model": "gpt-5.2"
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

## Known limitations (CTP reality)

- Generation quality varies run to run — hence the validation + retry safety net. A full
  generation takes 2–5 minutes (several sequential LLM calls; a retry doubles it). The status
  window shows the phase, elapsed time, and roll number.
- The Modify chat executes layout *commands* only; analytical questions fail with a JSON parse
  error, and data-source changes are out of its scope.
- Keep prompts free of data semantics that contradict the schema (e.g. discount formulas) —
  newer models refuse on contradictions, older ones silently pick a side.

## Tech Stack

- .NET 10.0 (`net10.0` / `net10.0-windows`, ReportDesigner uses the Razor SDK — the AI chat
  panel is a Blazor WebView)
- DevExpress XtraReports + AI Integration 26.1.3
- EF Core 8.0.18 + PostgreSQL (Npgsql 8)
- OpenAI SDK + Microsoft.Extensions.AI 10.x (any `IChatClient` provider is pluggable in principle;
  in practice the generation workflow is only reliable on gpt-5.2 today)
