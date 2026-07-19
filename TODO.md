# TODO

## P1: High

#### RPT-003: Add AI "Modify Report" chat behavior (ID: 1058)

The AI Assistant chat panel that edits an existing layout in natural language (add bands/tables,
restyle, group/sort/filter). WinForms-only, CTP, listed for 25.2+ — the feature that makes this
app more useful than the stock wizard. Attach alongside `ReportPromptToReportBehavior` in
`AIReportDesignerForm.OnLoad`.

Docs: https://docs.devexpress.com/XtraReports/405498 (Modify Report Behavior, WinForms, CTP).

## P2: Medium

#### RPT-004: Wire ReflectionSchemaDiscoveryService into the 26.1 cross-platform API (ID: 1059)

26.1 adds `PromptToReportRequest(userPrompt, dataSourceSchema, report)` +
`AIReportingIntegration.GeneratePromptToReportAsync()` — schema is a first-class parameter,
supports updating an existing report, runs headless. Our `[AIVisible]`/`[AIDescription]` curation
is still the value-add: it decides *what* the AI sees.

- a) Check the actual type of `PromptToReportRequest.DataSourceSchema` and map
  `ReflectionSchemaDiscoveryService` output onto it (replace `GenerateSystemPrompt()` text-blob
  as the schema carrier).
- b) Prototype a headless generation path (console or button): prompt → `GeneratePromptToReportAsync`
  → open in designer / save to `ReportDataV2`.
- c) Implement `IAIReportGenerationHost` for the clarification Q&A loop in that path.

Docs: https://docs.devexpress.com/XtraReports/405279 (v26.1 release notes, cross-platform API section).
Example: https://github.com/DevExpress-Examples/ai-powered-report-generation-in-console

## P3: Low

#### RPT-005: Blazor/Web Report Designer variant (ID: 1060)

Prompt-to-Report also exists in the Web Report Designer (ASP.NET Core & Blazor, CTP) — server-side
registration via `AddDevExpressAI` → `AddWebReportingAIIntegration` → `AddPromptToReportConverter()`.
Module + `ReportDataV2` storage are reusable as-is. Note: the Modify Report chat (RPT-003) is
WinForms-only; the web side has prompt-to-report, prompt-to-expression, AI test data, localization.
Only worth doing if a web-hosted designer becomes a real requirement.

Docs: https://docs.devexpress.com/XtraReports/405485 · Demo: https://demos.devexpress.com/blazor/AIPoweredExtensions/AIReportDesigner
