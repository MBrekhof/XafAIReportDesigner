# XafAIReportDesigner

Standalone AI-powered report designer built on DevExpress XtraReports and AI Integration. Generate reports from natural language prompts using a Northwind-style data model.

## Features

- AI Prompt-to-Report generation via DevExpress `ReportPromptToReportBehavior`
- Schema-aware predefined prompts (Order Summary, Product Catalog, Invoice reports)
- Load/Save reports to PostgreSQL database
- Full DevExpress Report Designer ribbon UI

## Prerequisites

- .NET 10.0 SDK
- PostgreSQL database
- OpenAI API key
- DevExpress 25.2.3 license

## Quick Start

1. Clone the repository
2. Create `XafAIReportDesigner/XafAIReportDesigner.ReportDesigner/appsettings.Development.json`:
   ```json
   {
     "OpenAI": {
       "ApiKey": "sk-your-key-here",
       "Model": "gpt-4o"
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

## Tech Stack

- .NET 10.0, DevExpress XtraReports + AI Integration 25.2.3
- EF Core 8.0.18 + PostgreSQL (Npgsql)
- OpenAI SDK + Microsoft.Extensions.AI
