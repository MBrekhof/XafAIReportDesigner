# TODO

## P1: High

#### RPT-001: Upgrade DevExpress 25.2.3 → 26.1 (ID: 1056)

Prereq for everything below — the multi-agent AI Report Wizard and the cross-platform
Prompt-to-Report API are 26.1-only. 26.1 is installed locally alongside 25.2.

- a) ~~Bump `DevExpress.AIIntegration.WinForms.Reporting`, `DevExpress.Win.Design`,
  `DevExpress.Persistent.Base`, `DevExpress.Persistent.BaseImpl.EFCore` to 26.1.x in both csproj files.~~
  Done on branch `rpt-001-devexpress-26.1` — all four at 26.1.3. EF Core stays 8.0.18 (exactly what
  26.1.3 depends on), Npgsql 8.x unchanged.
- b) ~~Check whether our `OpenAI 2.*` / `Microsoft.Extensions.AI.OpenAI 9.*` pins need to move.~~
  `Microsoft.Extensions.AI.OpenAI` bumped to `10.*` (resolves 10.3.0; the 9.* pin was silently
  lifted to 10.x by the dependency graph anyway). `OpenAI 2.*` (2.12.0) fine. Transitive
  `Microsoft.Extensions.AI` = 10.5.1, matching the 26.1 docs.
- c) Build + smoke-run done: designer launches on 26.1, ribbon + custom Database tab render,
  Report Design Analyzer 0 errors. **Remaining:** verify the AI wizard asks clarification questions
  (multi-agent flow) — needs a real OpenAI key; `appsettings.Development.json` is currently missing
  from the ReportDesigner project dir, recreate it per CLAUDE.md before testing.

#### RPT-002: Remove schema-stuffed predefined prompts (ID: 1057)

`AIReportDesignerForm.BuildPredefinedPrompts()` pastes the full schema text into every prompt —
a workaround for 25.x single-shot generation. The 26.1 wizard reads attached data-source metadata
itself and asks for what's missing.

- a) After RPT-001, test the wizard's "Add Data Source" flow against our PostgreSQL connection
  (`XafAIReportDesigner` XPO connection registered in `Program.cs`) — confirm it picks up
  tables/columns without the schema prompt.
- b) Slim the predefined prompts to short intent-only templates (Order Summary, Product Catalog,
  Invoice) without embedded schema.
- c) Move the "use connection named XafAIReportDesigner" hint to
  `behavior.Properties.PromptAugmentation` (one place) instead of repeating per prompt.
- d) Keep `ReflectionSchemaDiscoveryService` — see RPT-004 for its new role.

Docs: https://docs.devexpress.com/XtraReports/405460 (WinForms Prompt-to-Report, still CTP in 26.1).

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
