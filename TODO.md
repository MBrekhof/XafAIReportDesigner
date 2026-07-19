# TODO

## P1: High

## P2: Medium

#### RPT-006: Polish the headless generation path (ID: 1061)

Generation speed, benchmarked 2026-07-19 (invoice prompt, full multi-agent flow per roll):
**gpt-5.2 is the only model that reliably completes the DevExpress CTP workflow** —
2.5–4.5 min/roll, doubled when validation triggers the fresh retry. gpt-5.4-mini is ~50s but
produced an empty report once and a workflow-rejected layout once; gpt-5.6-luna refuses with
validation exceptions; gpt-5.6-terra gets HTTP 400 from the API (request-shape incompatibility).
Slowness is inherent for now; status window shows elapsed time + roll number. Recheck faster
models when DX ships 26.2 / de-CTPs the workflow. Also learned: prompt–schema contradictions
(prompt said `1 - discount`, schema says percent) make 5.6 models refuse outright and made
earlier models mis-compute — keep prompts semantics-free and let the schema carry meaning.

Follow-ups from RPT-004 (see DONE.md for the full recipe):

- a) ~~Page-break placement hint.~~ Added to the binding rules (keep header/table/totals
  together, break after totals); latest runs render cleanly one invoice per page.
- b) User-verified 2026-07-19: loading the generated RPT004-Invoice from the DB and modifying
  it with the AI Assistant chat "worked like a charm" — the full loop (headless generate →
  save → load with credential restore → chat edit) holds. **Still pending one click:** the
  in-app "Database → AI → Generate from Prompt" button itself (clarification/status dialogs;
  the console host variant is verified end to end including the validation warning path).
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
