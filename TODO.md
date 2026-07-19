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

## P2: Medium

#### RPT-008: Own modify path — spec-level edits + re-translate (ID: 1062)

Replace the DX CTP Modify chat (gpt-5.2-only, fails on structural edits: "move quantity to
first column" errored once, then claimed success without changes — 2026-07-19 testing) with the
own-pipeline trick applied to modification: persist the report's spec JSON alongside the saved
layout, let a small LLM call edit the SPEC ("move quantity first" = reorder the columns array),
then re-translate deterministically via `ReportSpecTranslator`. Reliable by construction, any
model, ~2s; false "I did it" becomes impossible. Interim workaround: re-run Generate from
Prompt with the tweak in the prompt (~4s). Design decision needed: where to persist the spec
(extra column on ReportDataV2 vs. side table vs. embedded in layout).

#### RPT-005: Blazor/Web Report Designer variant with the OWN pipeline (ID: 1060)

Host the mature (non-CTP) Web Report Designer (ASP.NET Core/Blazor wrapper) and wire OUR
pipeline server-side: a Generate endpoint calls `ReflectionSchemaDiscoveryService` +
`ReportSpecTranslator` (all Module, UI-free) and opens the result in the browser designer;
`ReportDataV2` storage reusable as-is. The own pipeline changed the calculus vs. the original
note: DX's web AI (CTP, gpt-5.2) is NOT needed — our generation is already headless and
provider-agnostic, so the web variant is mostly hosting plumbing (~1-2 days PoC). RPT-008's
modify path surfaces naturally as a text box in the web UI. Caveat: translator uses
System.Drawing fonts — fine on Windows hosting; a Linux container needs the DXFont/Skia swap.
Pursue when browser access or XAF-Blazor embedding becomes a real requirement.

Docs: https://docs.devexpress.com/XtraReports/405485 · Demo: https://demos.devexpress.com/blazor/AIPoweredExtensions/AIReportDesigner
