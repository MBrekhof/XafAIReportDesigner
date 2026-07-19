# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Git Rules

- Only push to `origin` (MBrekhof/XafAIReportDesigner).
- Always create feature branches off `master` — do not commit directly to `master`.

## Project Overview

Standalone WinForms AI-powered report designer built on DevExpress XtraReports 26.1 and AI
Integration. Three AI paths: the `ReportPromptToReportBehavior` wizard, the `ReportModifyBehavior`
chat (edits open layouts), and a headless pipeline via the 26.1 cross-platform API
(`GeneratePromptToReportAsync`) that feeds the AI a curated schema including FK relationships —
the only path that produces correct master-detail reports. Entity metadata is discovered via
reflection from a shared Module assembly containing Northwind-style business objects.

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

### Solution Structure (2 projects)

- **`XafAIReportDesigner.Module/`** — Shared library: EF Core entity definitions (Northwind domain), custom attributes (`[AIVisible]`, `[AIDescription]`), and `ReflectionSchemaDiscoveryService` for runtime entity discovery.
- **`XafAIReportDesigner.ReportDesigner/`** — WinForms app (net10.0-windows). Entry point: `Program.cs`. Main form: `AIReportDesignerForm` extends `XRDesignRibbonForm` with AI behavior attached via `BehaviorManager.Attach<ReportPromptToReportBehavior>()`.

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
- **`AIReportDesignerForm`** — Report Designer shell: attaches both AI behaviors
  (Temperature = 1 — GPT-5-series requirement), Database ribbon (Load/Save to PostgreSQL,
  headless Generate from Prompt with validation + fresh-retry keep-best),
  `AppConnectionStorageService` (wizard connection list + name-only serialization +
  load-time credential restore), `WinFormsAIReportGenerationHost` (clarification dialogs).
- **`ReportDbContext`** (inner class in AIReportDesignerForm) — Lightweight DbContext mapping only `ReportDataV2` for report storage.

### Hard-won constraints (verified, do not regress)

- Model must be **gpt-5.2** — only model that reliably completes the DX multi-agent workflow
  (benchmarked: mini/5.6 tiers break or refuse). Temperature must be 1 for GPT-5-series.
- Repair-style requests (`PromptToReportRequest` with an existing report) regenerate broadly AND
  mutate the passed instance — use fresh-roll + keep-best instead.
- Full recipes and gotchas: `DOCS/DONE.md` (RPT-001…RPT-004 entries).

### Database

- PostgreSQL for report storage and data queries
- 13 Northwind-style entities: Order, OrderItem, Customer, Product, Category, Supplier, Employee, EmployeeTerritory, Territory, Region, Shipper, Invoice, Enums
- Connection configured via `appsettings.json` or `appsettings.Development.json`

## Tech Stack

- .NET 10.0 (net10.0 / net10.0-windows; ReportDesigner uses `Microsoft.NET.Sdk.Razor` — the AI
  chat panel is a Blazor WebView)
- DevExpress XtraReports + AI Integration 26.1.3
- DevExpress Persistent Base/BaseImpl.EFCore 26.1.3, DevExpress.Reporting.Core 26.1.3 (Module)
- EF Core 8.0.18 + PostgreSQL (Npgsql 8)
- OpenAI SDK 2.x + Microsoft.Extensions.AI(.OpenAI) 10.x

## Configuration

Create `appsettings.Development.json` in the ReportDesigner project:
```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "Model": "gpt-5.2"
  },
  "Database": {
    "ConnectionString": "Host=localhost;Port=5432;Database=xafaireportdesigner;Username=xaf;Password=xaf123",
    "XpoConnectionString": "XpoProvider=Postgres;Server=localhost;Port=5432;User ID=xaf;Password=xaf123;Database=xafaireportdesigner;Encoding=UNICODE"
  }
}
```

The OpenAI API key is required. The app shows an error dialog if not configured.
