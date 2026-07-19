# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Git Rules

- Only push to `origin` (MBrekhof/XafAIReportDesigner).
- Always create feature branches off `master` — do not commit directly to `master`.

## Project Overview

Standalone WinForms AI-powered report designer built on DevExpress XtraReports 26.1. All AI
runs through the **own pipeline** (Database ribbon → Generate from Prompt / Modify via AI) —
any LLM via LLMTornado fills a report-spec JSON, the deterministic `ReportSpecTranslator`
(Module) builds the XtraReport; ~4s per generation. The DevExpress AI CTP integration
(wizard/chat behaviors) was evaluated and REMOVED 2026-07-19 — slow, gpt-5.2-only, unreliable
chat; recipes preserved in DOCS/DONE.md. Entity metadata is discovered via reflection from a
shared Module assembly containing Northwind-style business objects.

## Build & Run Commands

```bash
# Build the entire solution
dotnet build XafAIReportDesigner.slnx

# Run the Report Designer app (Windows only)
dotnet run --project XafAIReportDesigner/XafAIReportDesigner.ReportDesigner

# Build a specific project
dotnet build XafAIReportDesigner/XafAIReportDesigner.Module/XafAIReportDesigner.Module.csproj
```

There is no formal test suite.

## Architecture

### Solution Structure (3 projects)

- **`XafAIReportDesigner.Module/`** — Shared library: EF Core entity definitions (Northwind domain), custom attributes (`[AIVisible]`, `[AIDescription]`), and `ReflectionSchemaDiscoveryService` for runtime entity discovery.
- **`XafAIReportDesigner.ReportDesigner/`** — WinForms app (net10.0-windows). Entry point: `Program.cs`. Main form: `AIReportDesignerForm` extends `XRDesignRibbonForm`.
- **`XafAIReportDesigner.Web/`** — Blazor Server app (net10.0) hosting `DxReportDesigner` +
  the own pipeline (`AIReportService` → shared `SpecPipeline`). `ReportDataV2Storage`
  (ReportStorageWebExtension) shares the WinForms DB storage. Web gotchas (verified):
  `UseStaticWebAssets()` required outside Development; name-only connections resolve ONLY via
  `IConnectionProviderFactory`/`IConnectionProviderService` in DI (DefaultConnectionStringProvider
  is not consulted by the web preview). Run: `dotnet run --project XafAIReportDesigner/XafAIReportDesigner.Web` → http://localhost:5210 with AI behavior attached via `BehaviorManager.Attach<ReportPromptToReportBehavior>()`.

### Key Components

- **`ReflectionSchemaDiscoveryService`** (Module) — Scans the Module assembly for `[AIVisible]`
  entities; `GenerateDataSourceSchema()` emits factual PostgreSQL schema text (tables, columns,
  enums, FK graph) for `PromptToReportRequest.DataSourceSchema`. Skips computed get-only
  properties (no DB column).
- **`SchemaSqlDataSourceFactory`** (Module) — Builds the `SqlDataSource` matching the schema
  (query per table + named `MasterDetailInfo` relations in both FK directions, self-FKs skipped);
  `DescribeDataMembers()` emits the binding rules the AI must follow (absolute relation-name
  paths, one hop per DetailReportBand); `Attach()` attaches a data source to a generated report
  (snapshots band DataMembers first — assigning DataSource resets them); `ValidateBindings()`
  resolves every expression path against the schema/relation graph.
- **`ReportSpecTranslator`** (Module) — the own pipeline's deterministic half: spec records +
  `BuildSystemPrompt()` + `ParseSpec()` + `BuildReport()`. Encodes the proven band shapes (ONE
  root-level DetailReportBand with full absolute path and EXPLICIT DataSource; totals as
  top-level `Sum()` with `TextFormatString`) and `RepairChains()` (BFS over the relation graph
  repairs under-/over-qualified and wrong-direction field chains). `poc/generate-poc.cs` is the
  headless harness driving this exact code.
- **`AIReportDesignerForm`** — Report Designer shell: Database ribbon (Load/Save to
  PostgreSQL, Generate from Prompt + Modify via AI — shared RunSpecPipelineAsync, model
  dropdown, up to 3 rolls keep-best; the spec JSON rides in XtraReport.Extensions),
  `AppConnectionStorageService` (wizard connection list + name-only serialization +
  load-time credential restore).
- **`ReportDbContext`** (inner class in AIReportDesignerForm) — Lightweight DbContext mapping only `ReportDataV2` for report storage.

### Hard-won constraints (verified, do not regress)

- The own pipeline has no model restriction (default gpt-5.4-mini, `OpenAI:GenerateModel`).
  Historical: the removed DX CTP workflow ran ONLY on gpt-5.2 at Temperature 1 — if the DX AI
  integration is ever revisited, start from the DONE.md recipes, not from scratch.
- Repair-style requests (`PromptToReportRequest` with an existing report) regenerate broadly AND
  mutate the passed instance — use fresh-roll + keep-best instead.
- Full recipes and gotchas: `DOCS/DONE.md` (RPT-001…RPT-004 entries).

### Database

- PostgreSQL for report storage and data queries
- 13 Northwind-style entities: Order, OrderItem, Customer, Product, Category, Supplier, Employee, EmployeeTerritory, Territory, Region, Shipper, Invoice, Enums
- Connection configured via `appsettings.json` or `appsettings.Development.json`

## Tech Stack

- .NET 10.0 (net10.0 / net10.0-windows)
- DevExpress XtraReports 26.1.3 + LLMTornado (any-provider IChatClient)
- DevExpress Persistent Base/BaseImpl.EFCore 26.1.3, DevExpress.Reporting.Core 26.1.3 (Module)
- EF Core 8.0.18 + PostgreSQL (Npgsql 8)
- Microsoft.Extensions.AI abstractions (via LlmTornado.Microsoft.Extensions.AI)

## Configuration

Create `appsettings.Development.json` in the ReportDesigner project:
```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "GenerateModel": "gpt-5.4-mini"
  },
  "Database": {
    "ConnectionString": "Host=localhost;Port=5432;Database=xafaireportdesigner;Username=xaf;Password=xaf123",
    "XpoConnectionString": "XpoProvider=Postgres;Server=localhost;Port=5432;User ID=xaf;Password=xaf123;Database=xafaireportdesigner;Encoding=UNICODE"
  }
}
```

The OpenAI API key is required. The app shows an error dialog if not configured.
