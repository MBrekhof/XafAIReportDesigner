# Codex full code review — 2026-09-09

Job: task-mttmap6g-db5iyt (completed, 7m 2s). Codex session 01a0847e-6a60-75e0-a017-e641ea475612.
Static review only; no build, no live previews. Roslyn could not resolve the generated Razor `App` component, so compilation remains unverified.

**Verdict: changes are needed before relying on saved reports or generated financial output.** The main risks are unintended overwrites, lost manual edits, silently omitted report content, and invalid expressions passing validation.

Reviewed all 28 handwritten C# files, six Razor files, project configuration, and relevant database schema. No files changed.

## Module

1. **High — Incomplete JSON aborts the retry loop instead of triggering another attempt.**
   `ReportSpecTranslator.cs:81–94`, `SpecPipeline.cs:29–48`.
   `{}` can deserialize into a non-null spec with null collections. `BuildReport` subsequently dereferences them, escaping the loop. A later provider or translation exception also discards an already-selected best candidate.
   **Fix:** validate required strings, collections, elements, and relation paths before translation. Handle recoverable failures per attempt, preserving the best completed candidate.

2. **High — The translator silently drops or collapses content allowed by its specification.**
   `ReportSpecTranslator.cs:34–38,130–140,159–183`.
   Only the deepest level's columns are rendered; its `HeaderFields` are never rendered. Earlier levels' headers are placed in the root detail, although the prompt promises a header per row of that level. An invoice containing multiple orders therefore cannot receive the promised per-order headers.
   **Fix:** align the spec with the supported layout. Render required ancestor information/grouping within the single deep band, and explicitly reject unsupported level content instead of dropping it.

3. **High — Binding validation can declare malformed expressions and missing data contexts valid.**
   `SchemaSqlDataSourceFactory.cs:165–198,245–254`, `SpecPipeline.cs:41–48`.
   Validation checks bracketed field names without parsing the expression. For example, `[Quantity] +` passes the field check. Empty expressions and an empty root `DataMember` also produce no issue. The pipeline then stops on "zero issues."
   **Fix:** parse expressions using the reporting expression grammar, validate field operands from the parsed structure, and reject missing required data contexts. https://docs.devexpress.com/XtraReports/120104

4. **Medium — Chain repair can silently substitute a different business value.**
   `ReportSpecTranslator.cs:298–307`.
   Redundant-prefix removal runs before wrong-direction repair. In an `OrderItems` context, `[ProductsOrderItems].[UnitPrice]` becomes `[UnitPrice]` immediately, bypassing the available repair to `[OrderItemsProducts].[UnitPrice]`. This substitutes the order-line price for the product price, and validation accepts it.
   **Fix:** repair identifiable relation-direction errors first. Reject ambiguous repairs rather than dropping relationship information whenever a same-named local column exists.

5. **Medium — Labels and formats are interpolated into expression literals without escaping.**
   `ReportSpecTranslator.cs:261–272`.
   A label such as `Customer's reference` produces an invalid quoted expression. Custom formats containing apostrophes have the same problem.
   **Fix:** escape literal values through a single expression-literal helper, doubling embedded apostrophes or using an appropriate criteria formatter.

6. **Medium — `PagePerMasterRow` is ignored for reports without detail levels.**
   `ReportSpecTranslator.cs:220–221`.
   A master-only customer report with `PagePerMasterRow=true` receives no page-break configuration because `lastBand` is null.
   **Fix:** apply the break to the root detail when no deep band exists; retain the existing deep-band placement otherwise.

7. **Medium — Rejected and superseded report candidates are never disposed.**
   `SpecPipeline.cs:40–47`.
   Each attempt creates a report. A worse candidate is abandoned, and a better candidate overwrites the previous reference without cleanup. Exceptions also leave owned candidates undisposed.
   **Fix:** dispose rejected candidates immediately, dispose the previous winner when replacing it, and clean up on exceptional exits. Transfer ownership only for the returned winner.

8. **Low — Active prompt guidance contradicts the translator, while unused helpers retain obsolete repair advice.**
   `SchemaSqlDataSourceFactory.cs:68,91–118,120–125`, `ReportSpecTranslator.cs:115–118`.
   Both hosts send guidance requiring nested one-hop bands, while the translator explicitly implements the documented single-deep-band constraint. The validator comment recommends existing-report repair requests, contrary to `CLAUDE.md`. `Attach()` and `GenerateSystemPrompt()` have no callers in the solution or POC C# sources.
   **Fix:** remove obsolete CTP instructions from active prompts and comments; remove or explicitly isolate unused compatibility helpers.

## ReportDesigner

1. **High — "Save as" retains the previous layout name and can cause a subsequent save to overwrite the original.**
   `AIReportDesignerForm.cs:287–294,318–327,341`.
   The chosen name updates the database row but not `report.DisplayName` before serialization. Loading does not restore the row's name either. Open A, save as B, reload B, then accept the next save dialog's default: it can still propose A and overwrite it.
   **Fix:** track database identity per design document, synchronize its display name before serialization, and restore metadata on load.

2. **Medium — SQL query names are stored as XAF CLR type metadata.**
   `AIReportDesignerForm.cs:329–335,389–406`.
   `ExtractDataTypeName` selects the first SQL query regardless of the report's root data member. Generated sources contain every entity query, so an Orders report can receive `Categories`; neither value is the CLR full name XAF expects.
   **Fix:** resolve the actual root entity to its CLR `FullName`, or deliberately leave the association empty for SQL-only reports.

3. **Medium — Saving over a predefined report can announce success without saving the layout.**
   `AIReportDesignerForm.cs:326–335,349–351`.
   The overwrite lookup includes predefined XAF records. DevExpress 26.1 source confirms that their `Content` setter ignores assignments, yet this handler still displays success.
   **Fix:** reject predefined overwrite targets and create a separate runtime copy instead.

4. **Medium — Opening the report picker synchronously loads every layout blob on the UI thread.**
   `AIReportDesignerForm.cs:252–255,265,287–291`.
   The picker needs names and IDs, but `.ToList()` loads full entities, including every `Content` blob.
   **Fix:** asynchronously project untracked IDs/names, then fetch only the selected layout by ID.

5. **Low — Database saves leave the designer marked dirty.**
   `AIReportDesignerForm.cs:349–351`.
   Successful custom persistence never updates the originating panel's `ReportState`, so closing can still prompt for saving and enter the default file-save workflow.
   **Fix:** set that panel to `ReportState.Saved` after database persistence succeeds.

## Web

1. **High — "Generate a new report" silently overwrites an existing report with the same name.**
   `Home.razor:73–76`, `AIReportService.cs:68–71`, `ReportDataV2Store.cs:34–38`.
   Generation calls the same unconditional update operation as replacement. Reusing a name destroys the existing saved layout.
   **Fix:** use a create-only operation for generation that returns a name-conflict result; reserve replacement for an explicit update action.

2. **Medium — Creation accepts names that the designer subsequently rejects.**
   `ReportDataV2Storage.cs:14–15,35–38`, `Home.razor:75–76`.
   `Sales/2026` passes AI input validation and is saved, but fails `IsValidUrl`. `SetNewData` also saves without validation; DevExpress explicitly does not call `IsValidUrl` for this path.
   **Fix:** centralize creation validation, including length limits, or use stable IDs as URLs and treat names purely as display text.

3. **Medium — AI operations never dispose their temporary or completed reports.**
   `AIReportService.cs:38–48,61–75`.
   **Fix:** use `using` for the temporary report and guaranteed cleanup for the winning report after persistence. Dispose per-operation chat clients according to their ownership contract.

4. **Medium — Database failures during initialization/rendering bypass page error handling.**
   `Home.razor:67–70`, `Designer.razor:11`.
   An unavailable database throws before the button-operation `try/catch` can help. The designer also queries synchronously during rendering.
   **Fix:** load a cached list through guarded asynchronous initialization and render an explicit error/retry state.

5. **Low — Unresolved-binding warnings disappear immediately after generation.**
   `AIReportService.cs:73–76`, `Home.razor:91–95`.
   Results with unresolved bindings still return `Success=true`. The page assigns their diagnostics and immediately navigates away.
   **Fix:** retain the result screen when issues exist and offer an explicit "Open designer" action, or carry diagnostics into the designer.

## Cross-cutting

1. **High — Display names are unsafe persistence identities, with race conditions and inconsistent duplicate handling.**
   `ReportDataV2Store.cs:25–45`, `ReportDataV2Storage.cs:25–26`, `AIReportDesignerForm.cs:326–346`, `seed-postgres.sql:153`.
   Two first saves can both insert the same name; the seed schema has no unique name constraint. Web subsequently loads one matching row, updates all matches, and throws when `ToDictionary` encounters duplicates. WinForms updates only the first match.
   **Fix:** share persistence keyed by `ReportDataV2.ID`, with explicit create/update operations and conflict detection. If names remain identifiers, enforce database uniqueness and atomic writes across both hosts.

2. **High — AI modification rebuilds from stale embedded JSON and loses manual designer edits.**
   `AIReportDesignerForm.cs:141–160`, `AIReportService.cs:38–48`, `ReportSpecTranslator.cs:62–78`.
   Generate a report, manually add a logo or change its filter, then request an AI column change. Both hosts reconstruct from the original spec without incorporating those edits. Web immediately overwrites the saved layout with that reconstruction.
   **Fix:** detect layout/spec divergence and reject unsupported regeneration or preserve a separate version. Longer term, reconcile supported designer changes into the spec.

3. **Low — Duplicated host services already implement different persistence policies.**
   `AIReportDesignerForm.cs:190–198,329–345,428–431`, `AIReportService.cs:54–63,79–81`, `ReportDataV2Store.cs:34–42`.
   Chat-client creation, prompt preparation, connection conversion, and storage policy are duplicated. WinForms updates type metadata; Web inserts empty metadata and never updates it. Overwrite semantics differ.
   **Fix:** share orchestration, connection construction, and report persistence behind host-independent services.

No active CTP repair-style request was found: both hosts call the fresh-report `SpecPipeline`. The translator also retains the explicit data source on its single deep band.

## Prioritized top five actions

1. **Fix persistence identity and overwrite semantics:** shared ID-based storage, create-only generation, conflict detection, and correct WinForms Save As behavior.
2. **Protect manual edits:** detect stale specs and preserve report versions before AI replacement.
3. **Strengthen validation and retries:** validate JSON shape, expression syntax, data contexts, and recoverable per-attempt failures.
4. **Correct translation coverage:** render or reject every supported spec field and eliminate ambiguous chain repairs.
5. **Close resource ownership gaps:** dispose rejected candidates, temporary reports, and serialized Web results.
