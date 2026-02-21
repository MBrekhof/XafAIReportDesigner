# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Git Rules

- Only push to `origin` (MBrekhof/XafAIReportDesigner).
- Always create feature branches off `master` — do not commit directly to `master`.

## Project Overview

Standalone WinForms AI-powered report designer built on DevExpress XtraReports and AI Integration. Uses the DevExpress `ReportPromptToReportBehavior` to generate reports from natural language prompts. Entity metadata is discovered via reflection from a shared Module assembly containing Northwind-style business objects.

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

- **`ReflectionSchemaDiscoveryService`** — Scans the Module assembly for `[AIVisible]` entities and builds a schema prompt for the AI, including table/column names and relationships.
- **`AIReportDesignerForm`** — Extends the DevExpress Report Designer with AI prompt-to-report, plus Database ribbon buttons for Load/Save reports to PostgreSQL.
- **`ReportDbContext`** (inner class in AIReportDesignerForm) — Lightweight DbContext mapping only `ReportDataV2` for report storage.

### Database

- PostgreSQL for report storage and data queries
- 13 Northwind-style entities: Order, OrderItem, Customer, Product, Category, Supplier, Employee, EmployeeTerritory, Territory, Region, Shipper, Invoice, Enums
- Connection configured via `appsettings.json` or `appsettings.Development.json`

## Tech Stack

- .NET 10.0 (net10.0 / net10.0-windows)
- DevExpress XtraReports + AI Integration 25.2.3
- DevExpress Persistent Base/BaseImpl.EFCore 25.2.3
- EF Core 8.0.18 + PostgreSQL (Npgsql)
- OpenAI SDK + Microsoft.Extensions.AI

## Configuration

Create `appsettings.Development.json` in the ReportDesigner project:
```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "Model": "gpt-4o"
  },
  "Database": {
    "ConnectionString": "Host=localhost;Port=5432;Database=xafaireportdesigner;Username=xaf;Password=xaf123",
    "XpoConnectionString": "XpoProvider=Postgres;Server=localhost;Port=5432;User ID=xaf;Password=xaf123;Database=xafaireportdesigner;Encoding=UNICODE"
  }
}
```

The OpenAI API key is required. The app shows an error dialog if not configured.
