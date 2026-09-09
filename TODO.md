# TODO

**Status: ACTIVE (2026-09-09).** The own provider-agnostic AI pipeline is merged to master
(Generate + Modify via AI, any model, ~4s; DX AI CTP fully removed — RPT-007..009 in DONE.md).
The Blazor/Web variant (RPT-005) is merged; UI refinement deferred (RPT-010). The Codex code
review round (RPT-011..016) is merged; the web UI refinement (RPT-010, concept in
`DOCS/RPT-010-concept.md`) is on branch `rpt-010-web-ui` — see DONE.md.
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

## P2: Medium

#### RPT-017: Master-only specs render as stacked labels, not a table (ID: 1587)

Seen 2026-09-09 during the RPT-010 smoke test: "A list of customers with company name, city
and country, one row per customer" produces a spec with `masterView: Customers`, four
`masterFields` and no `levels`. `ReportSpecTranslator.BuildReport` puts masterFields as one
label per line in the root Detail band (the shape meant for an invoice header), so the
report reads as a stacked block per customer instead of a table with a header row. The
translator only builds an `XRTable` for the innermost level.

Fix (translator only, no prompt change):
- `Levels` empty and `pagePerMasterRow` false → render masterFields as columns: header row
  with the labels, root Detail band with one `XRTable` row of the expressions (reuse
  `BuildRow`, `FieldSpec` → `ColumnSpec`, `Label ?? expression` as header, right-align
  numeric/currency formats). `pagePerMasterRow` true keeps the stacked "one page per record"
  shape.
- Totals with no levels currently attach to nothing (`lastBand == null`) — put them in a
  ReportFooter for the table shape.
- `poc/translator-check.cs`: a flat spec yields an XRTable with N cells in Detail; a
  pagePerMasterRow spec keeps stacked labels.

Ceiling: a flat list wider than ~8 columns gets cramped at 650pt — same as the level table.
