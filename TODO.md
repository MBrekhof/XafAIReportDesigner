# TODO

**Status: ACTIVE — own pipeline productized on branch `poc-own-pipeline` (2026-07-19,
RPT-007 in DONE.md).** "Generate from Prompt" now runs the own provider-agnostic pipeline
(`ReportSpecTranslator` in the Module, model dropdown in the ribbon, default gpt-5.4-mini,
~4s per generation). Branch awaits merge to master. The parking rationale below applies only
to the abandoned DX-CTP generation path (wizard/chat remain as secondary paths).

**Old status: PARKED (2026-07-19).** The exploration succeeded — the full pipeline works and is
documented — but the result is not near useful as a product: generation takes 2.5–5 min per
roll, only gpt-5.2 completes the DevExpress CTP workflow, and quality varies run to run behind
a validation/retry safety net. Everything is pushed, documented (`README.md`, `DOCS/DONE.md`),
and preserved in project memory.

**Revive when any of these change:**
- DevExpress ships 26.2 / takes the reporting AI out of CTP (wider model support, stable
  workflow — rerun the model benchmark in the scratch scripts first).
- A faster model reliably completes the generation workflow (retest gpt-5.6+ tiers, Anthropic
  once DX lists it for *reporting*, or LLMTornado as a multi-provider benchmarking harness).
- A real business need for AI report generation appears in another project — the Module
  (`ReflectionSchemaDiscoveryService`, `SchemaSqlDataSourceFactory` incl. `ValidateBindings`)
  is UI-free and reusable as-is.

## P3: Low

#### RPT-005: Blazor/Web Report Designer variant (ID: 1060)

Prompt-to-Report also exists in the Web Report Designer (ASP.NET Core & Blazor, CTP) — server-side
registration via `AddDevExpressAI` → `AddWebReportingAIIntegration` → `AddPromptToReportConverter()`.
Module + `ReportDataV2` storage are reusable as-is. Note: the Modify Report chat is
WinForms-only; the web side has prompt-to-report, prompt-to-expression, AI test data, localization.
Only worth doing if a web-hosted designer becomes a real requirement.

Docs: https://docs.devexpress.com/XtraReports/405485 · Demo: https://demos.devexpress.com/blazor/AIPoweredExtensions/AIReportDesigner
