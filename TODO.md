# TODO

## P1: High

## P2: Medium

#### RPT-006: Polish the headless generation path (ID: 1061)

Follow-ups from RPT-004 (see DONE.md for the full recipe):

- a) Page-break placement: the AI puts an invoice's header and its items table in separate
  DetailReportBands, so the break can land between them. Add a hint to the binding-rules text
  ("keep a master row's header, detail table and totals together; break after totals") or
  post-process PageBreak settings.
- b) The in-app "Database → AI → Generate from Prompt" button and the WinForms clarification
  dialog need a user click-through (the console host variant is verified end to end).
- c) ~~Regenerate RPT004-Invoice after the Discount `[AIDescription]` fix.~~ Verified 2026-07-19:
  all line totals correct (Chang 5×£19 at 5% = £90.25, was −€380), subtotals/VAT add up,
  "No items" caption on empty invoices. Saved to ReportDataV2 as "RPT004-Invoice".
- d) Run-to-run layout variance (CTP): the regenerated report has empty invoice-header fields
  (", Invoice: Date: Due:") that the previous generation filled correctly. Consider adding a
  header-binding example to the binding-rules text (scalar fields of the current master row:
  plain [InvoiceNumber], [InvoiceDate]) or a validation pass that flags unbound labels.

## P3: Low

#### RPT-005: Blazor/Web Report Designer variant (ID: 1060)

Prompt-to-Report also exists in the Web Report Designer (ASP.NET Core & Blazor, CTP) — server-side
registration via `AddDevExpressAI` → `AddWebReportingAIIntegration` → `AddPromptToReportConverter()`.
Module + `ReportDataV2` storage are reusable as-is. Note: the Modify Report chat (RPT-003) is
WinForms-only; the web side has prompt-to-report, prompt-to-expression, AI test data, localization.
Only worth doing if a web-hosted designer becomes a real requirement.

Docs: https://docs.devexpress.com/XtraReports/405485 · Demo: https://demos.devexpress.com/blazor/AIPoweredExtensions/AIReportDesigner
