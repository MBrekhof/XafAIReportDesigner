# TODO

**Status: ACTIVE (2026-07-19).** The own provider-agnostic AI pipeline is merged to master
(Generate + Modify via AI, any model, ~4s; DX AI CTP fully removed — RPT-007..009 in DONE.md).
The Blazor/Web variant (RPT-005) is merged; UI refinement deferred (RPT-010).
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

## P1: High

Codex full code review 2026-09-09 (`DOCS/CODEX-REVIEW-2026-09-09.md`) — findings verified in
code and regrouped by fix locality; each card carries the own assessment.

#### RPT-011: Report persistence — name-as-identity overwrites and Save-as bug (ID: 1581)

Source: Codex review findings Web#1, ReportDesigner#1, #3, Web#2, Cross-cutting#1. Verified.

Confirmed (own assessment):
- Web `AIReportService.RunAsync` → `ReportDataV2Store.Save` is UPDATE-then-INSERT. "Generate a
  new report" with an existing name silently replaces that report's layout. Real data loss,
  1-line guard (`store.Load(name) != null` → fail).
- WinForms `OnSaveToDatabase` saves under `reportName` but never sets
  `report.DisplayName = reportName`, so the next Save dialog defaults to the OLD name and
  overwrites the original. 1-line fix before `SaveLayoutToXml`.
- `ReportDataV2.DisplayName` has no unique index (seed-postgres.sql); WinForms `FirstOrDefault`,
  Web `UPDATE … WHERE DisplayName` hits all rows and `GetUrls().ToDictionary` throws on a
  duplicate → web designer dead until cleaned up.
- WinForms overwrite lookup doesn't exclude `IsPredefined` rows. Codex says (from installed DX
  26.1 source, NOT verified by me — dxdocs silent) the predefined `Content` setter is a no-op,
  so the UI would report success without saving. Filter `!r.IsPredefined` regardless.
- Web: `Sales/2026` is accepted on Generate but rejected by `IsValidUrl` → saved report can't
  be opened in the designer.

Not adopting from Codex: the "persistence keyed by ID with explicit create/update + conflict
detection" redesign. Single-user demo tool; unique index + create-only Generate + DisplayName
sync covers every listed failure.

Plan:
- a) Unique index on `ReportDataV2.DisplayName` (seed-postgres.sql + one-off on the dev DB).
- b) Web: Generate = create-only (fail on existing name); Modify keeps UPDATE. Apply the
  `IsValidUrl` rule in `Home.razor` before generating.
- c) WinForms: `report.DisplayName = reportName` before serialize; exclude `IsPredefined` from
  the overwrite lookup.

#### RPT-012: ReportSpecTranslator drops innermost headerFields, mis-repairs chains, no literal escaping (ID: 1582)

Source: Codex review Module#2, #4, #5, #6. All four verified in
`ReportSpecTranslator.BuildReport` / `RepairChains`.

Confirmed (own assessment):
- Innermost level's `headerFields` are never rendered (`spec.Levels.Take(Count-1)` collects
  headers; the deepest level only gets `Columns`). The prompt tells the LLM headerFields are
  "shown once per row of THIS level", so a 1-level spec (the common case) silently loses
  whatever the model put there. Intermediate levels' headers land in the root Detail (once per
  master row), not per row of that level — that part is the documented single-deep-band
  constraint (RPT-007) and stays. Fix is in the PROMPT, not the layout: tell the model the
  innermost level has no headerFields (use columns) and intermediate headerFields print once
  per master row. Then the translator matches what it promises.
- Repair order: over-qualified stripping runs before wrong-direction repair. From an
  OrderItems context, `[ProductsOrderItems].[UnitPrice]` becomes `[UnitPrice]`
  (OrderItems.UnitPrice) instead of `[OrderItemsProducts].[UnitPrice]` — different business
  value, passes validation. Move `RepairSegments` above the drop-leading loop (3-line
  reorder). Codex's "reject ambiguous repairs" is over-engineering; the reorder removes the
  ambiguity.
- Literal escaping: `WithLabel` emits `'{Label}: '` and `Formatted` emits `'{fmt}'` raw. A
  label containing an apostrophe (`Customer's ref`) breaks the expression. Double `'` → `''`
  in one helper.
- `PagePerMasterRow` ignored when `Levels` is empty (`lastBand != null` guard). Fall back to
  `rootDetail.PageBreak = AfterBand`.

Check to leave behind: extend `poc/generate-poc.cs` (or a 20-line self-check) with a 1-level
spec carrying headerFields + an apostrophe label + the ProductsOrderItems chain, asserting the
produced expressions.

## P2: Medium

#### RPT-013: SpecPipeline robustness — null spec collections, exception loses best roll, syntax check, disposal (ID: 1583)

Source: Codex review Module#1, #3, #7. Verified in `SpecPipeline.RollBestAsync`,
`ReportSpecTranslator.ParseSpec`, `SchemaSqlDataSourceFactory.ValidateBindings`.

Confirmed (own assessment):
- `ParseSpec` returns a non-null `ReportSpec` for `{}` or a partial object (positional record,
  STJ passes null for missing members). `BuildReport` then NREs on `spec.MasterFields`, the
  exception escapes the 3-roll loop and the host shows "Operation failed" — even if roll 1
  already produced a valid best. Likelihood low (models rarely emit `{}`), cost trivial: treat
  null `MasterView`/collections as parse failure, and wrap translate+validate per attempt so a
  throwing roll counts as a failed roll instead of aborting.
- `ValidateBindings` only checks bracketed chains resolve; `[Quantity] +` or an unbalanced
  `FormatString(` passes with zero issues and the pipeline stops rolling. Cheap syntax gate:
  `DevExpress.Data.Filtering.CriteriaOperator.Parse(expr)` in a try/catch inside
  `ValidateExpression` — the same grammar the report evaluates. The existing regex chain walk
  stays for field resolution.
- Losing candidates in the keep-best loop are never disposed. XtraReport is a Component; on a
  desktop app with <=3 candidates this is cosmetic, but `Dispose()` on the loser is one line.

Skipping: Codex Web#3 (dispose the temporary report in `ModifyAsync` and the winner after
serialization) — do it in the same pass if touching AIReportService, else leave.

#### RPT-014: Strip stale DX-CTP guidance from prompts, dead helpers, junk DataTypeName (ID: 1584)

Source: Codex review Module#8, ReportDesigner#2. Verified.

Confirmed (own assessment):
- `SchemaSqlDataSourceFactory.DescribeDataMembers` is appended to every system prompt by both
  hosts, but half of it is band-layout instructions for the removed DX CTP flow ("Set the
  report's DataMember…", "NEST two DetailReportBands", "PageBreak = AfterBand"). The LLM now
  only emits a spec; the translator decides bands. Noise at best, contradicts the
  single-deep-band shape at worst. Keep the expression rules + the relation list, drop the rest.
- `ValidateBindings` doc comment still recommends "feed non-empty results back through a
  repair request" — the exact thing CLAUDE.md forbids. Fix the comment.
- `SchemaSqlDataSourceFactory.Attach()` and `ReflectionSchemaDiscoveryService.GenerateSystemPrompt()`
  have zero callers. Delete; DONE.md keeps the recipe.
- `AIReportDesignerForm.ExtractDataTypeName` stores the FIRST SqlDataSource query name (e.g.
  `Categories` for an Orders report). XAF expects a CLR full name in `DataTypeName`. Resolve
  `report.DataMember` root → `schema.FindEntity(...).ClrType.FullName`, or store empty like the
  Web host does. Only matters if a real XAF app ever reads this table, which is the stated
  point of using ReportDataV2.

#### RPT-015: Modify via AI discards manual designer edits (spec/layout divergence) (ID: 1585)

Source: Codex review Cross-cutting#2. Verified.

Assessment: this is the RPT-008 design, not a bug: Modify re-translates the embedded spec, so
anything done by hand in the designer after generation (logo, filter, moved control) is not
in the spec and disappears from the result. Codex rates it High; I rate it a known trade-off
that needs a guard, not a redesign.

Host behaviour differs and that is the real problem:
- WinForms `OnModifyViaAI` opens the result as a NEW document — the edited original stays
  open; nothing is lost until the user saves over it.
- Web `ModifyAsync` → `store.Save(reportName, …)` overwrites the saved layout in place. Manual
  edits saved from the web designer are gone with no warning.

Cheapest guard (decide before building): at `AttachSpec` time also store a hash of the
serialized layout in `Extensions`. On Modify, if the current layout hash differs, warn
(WinForms MessageBox / Web status) that manual edits will not carry over — and in Web, save
the result under `<name> (AI)` instead of overwriting. Reconciling designer edits back into
the spec is out of scope.

## P3: Low

#### RPT-016: One ReportDataV2 store for both hosts + small host hygiene (ID: 1586)

Source: Codex review Cross-cutting#3, ReportDesigner#4, #5, Web#4. Verified.

Assessment: WinForms uses an EF `ReportDbContext` (inner class), Web uses raw Npgsql
`ReportDataV2Store`; they already disagree on overwrite semantics and metadata (WinForms
writes DataTypeName, Web writes ''). Codex wants host-independent orchestration, connection
construction and persistence services. Take the half that pays: move `ReportDataV2Store` into
the Module and have WinForms call it; drop `ReportDbContext`. Leave chat-client creation and
prompt assembly duplicated — 6 lines each, not worth an abstraction.

Bundle while in there:
- WinForms picker `.ToList()` loads every `Content` blob to show names. Project
  `(ID, DisplayName)`, load the chosen blob by ID.
- After a DB save the designer stays dirty (close prompts to save to file). Codex points at
  `XRDesignPanel.ReportState = ReportState.Saved` — NOT verified against dxdocs yet.
- Web `Home.razor OnInitialized` and `Designer.razor` call `Store.ListNames()` synchronously
  during render; a DB outage surfaces as a Blazor error boundary instead of a message. Low;
  wrap in try/catch + status line when touching the page (or fold into RPT-010).

Depends on RPT-011 landing first (it changes the Save semantics this card would move).

#### RPT-010: Web UI refinement (ID: 1063)

The web variant works (user-confirmed 2026-07-19) but the home page is bare-bones: plain
HTML controls, no progress animation during generation (~5s of button-disabled silence), no
report thumbnails/cards, top bar is minimal. Refine when the web variant becomes a daily
tool — candidates: DevExpress Blazor components (DxButton/DxComboBox/DxLoadingPanel) for a
consistent look with the designer, generation status streaming (SpecPipeline already emits
per-attempt status), report list with delete, and a proper landing layout. Deliberately
deferred by the user ("works, ui needs some refinement, not now").
Codex review 2026-09-09 (Web#5): after a Generate with unresolved bindings the page navigates
to the designer immediately, so the warning list is never seen — keep the result on screen
with an explicit "Open designer" button when `Issues` is non-empty.

