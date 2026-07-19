# TODO

**Status: ACTIVE (2026-07-19).** The own provider-agnostic AI pipeline is merged to master
(Generate + Modify via AI, any model, ~4s; DX AI CTP fully removed — RPT-007..009 in DONE.md).
The Blazor/Web variant (RPT-005) is built on branch `rpt-005-web-designer`, awaiting merge.
The parking note below is historical (abandoned DX-CTP path).

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

#### RPT-010: Web UI refinement (ID: 1063)

The web variant works (user-confirmed 2026-07-19) but the home page is bare-bones: plain
HTML controls, no progress animation during generation (~5s of button-disabled silence), no
report thumbnails/cards, top bar is minimal. Refine when the web variant becomes a daily
tool — candidates: DevExpress Blazor components (DxButton/DxComboBox/DxLoadingPanel) for a
consistent look with the designer, generation status streaming (SpecPipeline already emits
per-attempt status), report list with delete, and a proper landing layout. Deliberately
deferred by the user ("works, ui needs some refinement, not now").

