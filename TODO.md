# TODO

## P1: High

## P2: Medium

#### RPT-006: Polish the headless generation path (ID: 1061)

Follow-ups from RPT-004 (see DONE.md for the full recipe):

- a) ~~Page-break placement hint.~~ Added to the binding rules (keep header/table/totals
  together, break after totals); latest runs render cleanly one invoice per page.
- b) The in-app "Database → AI → Generate from Prompt" button and the WinForms clarification
  dialog need a user click-through (the console host variant is verified end to end, including
  the validation warning path).
- c) ~~Regenerate RPT004-Invoice after the Discount `[AIDescription]` fix.~~ Verified 2026-07-19:
  all line totals correct (Chang 5×£19 at 5% = £90.25, was −€380), subtotals/VAT add up,
  "No items" caption on empty invoices. Saved to ReportDataV2 as "RPT004-Invoice".
- d) ~~Run-to-run layout variance mitigation.~~ Implemented 2026-07-19:
  `SchemaSqlDataSourceFactory.ValidateBindings()` resolves every expression path and band
  DataMember against the schema/relation graph; on issues the app rolls ONE fresh generation
  and keeps the better result, then warns about anything left. Verified live: a 9-issue roll
  was auto-replaced by a 0-issue retry (clean headers, correct totals, saved to DB).
  **Finding:** the repair-style request (`PromptToReportRequest` with the existing report)
  regenerates broadly AND mutates the passed instance — measured strictly worse (9 → 37
  issues); fresh-roll-keep-best is the right mechanism.

## P3: Low

#### RPT-005: Blazor/Web Report Designer variant (ID: 1060)

Prompt-to-Report also exists in the Web Report Designer (ASP.NET Core & Blazor, CTP) — server-side
registration via `AddDevExpressAI` → `AddWebReportingAIIntegration` → `AddPromptToReportConverter()`.
Module + `ReportDataV2` storage are reusable as-is. Note: the Modify Report chat (RPT-003) is
WinForms-only; the web side has prompt-to-report, prompt-to-expression, AI test data, localization.
Only worth doing if a web-hosted designer becomes a real requirement.

Docs: https://docs.devexpress.com/XtraReports/405485 · Demo: https://demos.devexpress.com/blazor/AIPoweredExtensions/AIReportDesigner
