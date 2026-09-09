# RPT-010 concept — Web UI refinement (home page + designer chrome)

Status: IMPLEMENTED 2026-09-09 on branch `rpt-010-web-ui` (commits 322a97f, 268eb53), Codex-reviewed. Kept as the design record; deviations from the text are listed under "As built". Card: RPT-010 (ID 1063), deferred by the
user on 2026-07-19 ("works, ui needs some refinement, not now"). v1 was reviewed by Codex the
same day; v2 folds in that review (see "Review history" at the end). This is the design to
approve before any code is written.

## Goal

Make the web host usable as a daily tool for one person: find a report fast, generate or
modify one without guessing what is happening, land in the designer with the warnings still
readable, and get back. Nothing more.

**Non-goals:** authentication, multi-user, Linux hosting, report thumbnails, Modify from
inside the designer, cancelling a running LLM call (the last two were in v1 and are
deferred for the reasons under D3 and "Progress").

## Current state (facts, master 5106726)

- `Home.razor`: plain HTML — model `<select>`, prompt `<textarea>`, "Save as" `<input>`,
  Generate button; a second block with a report `<select>`, change `<textarea>`, Modify
  button; a status line and an issues list. Success navigates straight to
  `/designer?report=<name>`, so the unresolved-binding warnings are never seen (Codex Web#5).
- `Designer.razor`: `DxReportDesigner` full-height, `AllowMDI=false`; without a `report`
  query string it lists names as links.
- `MainLayout.razor`: a dark top bar (`#2c3e50`, the app's own colour, not the designer's)
  with two links. All CSS is a `<style>` block in `App.razor` (~10 rules). No `wwwroot`.
- Hosting is `InteractiveServer`: every UI event is a SignalR round trip; "no round trip"
  is never true, "no database query" is.
- Progress: `SpecPipeline.RollBestAsync` emits status text through a callback
  (`Attempt n: requesting report spec…`, `Attempt n: translating spec…`,
  `Attempt n failed: …`). There is no "validating" step text, and a malformed-spec retry
  emits nothing. `Home.razor` shows the text on one status line.
- Packages: `DevExpress.Blazor.Reporting.JSBasedControls` + `DevExpress.AspNetCore.Reporting`.
  `DevExpress.Blazor` (DxButton/DxComboBox/DxGrid…) is **not** referenced; the reporting
  package does not bring the component suite along.
- Store API (Module, after RPT-016): `ListNames`, `Find`, `Exists`, `Load`, `Insert`, `Save`.
  All synchronous ADO calls. No delete. `ReportDataV2` has no metadata columns (no
  "AI-generated" flag, no timestamps) and XAF owns its shape.
- `ReportDataV2Storage.SetNewData` (the designer's Save As) delegates to `Save`, i.e. an
  upsert: Save As onto an existing name overwrites it. This contradicts RPT-011's "names are
  identities" and is fixed as part of this card (see Service changes).
- `LlmTornado.Microsoft.Extensions.AI` 1.1.64 (installed): the non-streaming
  `GetResponseAsync` ignores its `CancellationToken` (Codex decompiled it; the local
  LlmTornado source agrees). A token passed through the pipeline would not stop the call.

## Decisions (recommendation first; Codex verdict in brackets)

### D1 — UI toolkit: DevExpress.Blazor where possible  [owner decision, overrides the v1–v4 recommendation]

The owner chose the DevExpress component suite for a consistent look with the designer.
`DevExpress.Blazor` 26.1.3 (same version as the reporting packages; in the local NuGet cache —
the local feed has moved on to 26.1.4, so a fresh machine needs either the cache or a
version bump of every DX package together). Setup per the 26.1 docs: `AddDevExpressBlazor()`
in Program.cs, `@using DevExpress.Blazor` in `_Imports.razor`,
`@DxResourceManager.RegisterTheme(Themes.Fluent)` in the App.razor head (RegisterScripts is
already there). Mapping:

| Surface | Component |
|---|---|
| report list + filter | `DxGrid` (`ShowSearchBox`), template column for badge and actions |
| buttons | `DxButton` |
| model dropdown, report picker | `DxComboBox` |
| prompt / change text | `DxMemo`; report name `DxTextBox` |
| progress | `DxLoadingPanel` over the form + the status line and timer |
| delete confirm | `DxPopup` with two `DxButton`s (modal, Escape closes — replaces the `<dialog>` notes) |
| collapsible New panel | `DxFormLayout` group with `Expanded` toggling, or a plain `<details>` |

Plain HTML stays only where a DX component adds nothing (badge span, status line).

### D2 — Two pages: home = report gallery + AI panel; designer = designer + slim bar  [agree]

- `/` **Reports** — the list is the primary object; Generate and Modify act on it.
- `/designer?report=<name>` **Designer** — `DxReportDesigner` plus a one-line app bar
  ("← Reports", current name). The name-less `/designer` listing goes away: `/designer`
  without `report` redirects to `/` with history replacement. A `report` that does not exist
  shows an error line with the back link instead of the designer's own failure.

### D3 — Modify only from the home page; nothing AI-related inside the designer  [v1 rejected]

v1 put "Modify via AI…" in the designer's app bar. Codex's objection is correct and fatal:
`ModifyAsync` loads the **stored** layout, so edits made in the designer but not yet saved
are invisible to the fingerprint (RPT-015) — the AI result could replace the saved layout
and "Open" would then discard the browser's unsaved work. Fixing that needs dirty-state and
current-report synchronisation with the DX client (`ReportSaved` is a save notification, not
dirty tracking; Save As changes the name under the app bar's feet). Not worth it for a
single-user tool. **Modify lives on the home page only**, where the stored layout is the
only layout. The designer's bar has the back link and the name, nothing else.

### D4 — Delete: yes, with confirm; no rename  [agree]

Delete per card, confirm `<dialog>`, `DELETE … WHERE DisplayName=@n AND NOT IsPredefined`
so the protection is in the statement. No rename: names are identities (RPT-011); the
designer's Save As covers "copy under a new name" once it is create-only (Service changes).

### D5 — Thumbnails: no  [agree]

A thumbnail needs a server-side render per report and a cache the table cannot hold. Names
plus an "AI" badge carry the list at this size.

## Design

### Home `/` — "Reports"

```
┌ AI Reports ──────────────────────────────────── model [gpt-5.4-mini ▾] ┐
│  [ + New report from prompt ]                    filter: [________]    │
│  ┌───────────────────────────┐  ┌───────────────────────────┐          │
│  │ Invoice per customer  AI  │  │ Product overview          │          │
│  │ Open   Modify   Delete    │  │ Open   Delete             │   …      │
│  └───────────────────────────┘  └───────────────────────────┘          │
│  ┌ New report ────────────────────────────────────────────────────┐    │
│  │ Describe the report… [textarea]                                 │    │
│  │ Save as [__________]                         [Generate]         │    │
│  └────────────────────────────────────────────────────────────────┘    │
└────────────────────────────────────────────────────────────────────────┘
```

- **List renders from `ListNames()` immediately** (names + `IsPredefined`, one query).
  Badges are filled in afterwards by a background pass that loads each layout once and
  sets `AI` when the spec extension is present; a layout that fails to parse simply gets no
  badge. No caching across page loads — the pass re-runs on each visit, which is what keeps
  it correct after saves from the designer or WinForms. Named ceiling: with dozens of large
  layouts on a remote DB the badges arrive late; the list itself never waits for them.
- **No "edited" badge.** Absence of one would read as "unedited", and RPT-015's detection is
  structural only (font/colour edits and pre-RPT-015 layouts escape it). The edit check runs
  where it matters: at Modify time, with the result shown in the result panel.
- **Card actions:** Open (→ designer), Modify (only on `AI` cards; opens the Modify panel
  under the card), Delete (confirm). Predefined rows: Open only, lock icon.
- **Filter box:** substring filter over the loaded list, server-side (Blazor Server), no
  DB query.
- **New report** panel collapsed by default (`<details>`). Model dropdown in the top bar
  applies to Generate and Modify alike.
- **One run at a time:** a single `Busy` flag on the page disables Generate, every Modify,
  every Delete **and every Open** while a run is in flight (the top-bar "Designer" link too).
  Prompt/change text is retained after a failure. This is a courtesy lock, not the
  guarantee — a second tab or the URL bar bypasses it; the guarantee is the pre-save
  recheck under Service changes.

### Progress (shared by Generate and Modify)

The existing status text, unchanged, plus an elapsed timer and a spinner, in a boxed panel
that replaces the form while `Busy`:

```
┌ Generating "Invoice per customer" ──────────────────────────┐
│ ◌ Attempt 2: translating spec…                     00:07     │
│   Attempt 1 failed: 429 Too Many Requests (rate limited)     │
└─────────────────────────────────────────────────────────────┘
```

- Failed-attempt lines accumulate; the current line is whatever `setStatus` last sent.
  A malformed-spec retry emits no status at all (the pipeline just rolls again), so the
  line simply stays on "requesting report spec…" a little longer. No parsing into stages (v1 had a three-step checklist; the pipeline has no "validating"
  step and silent retries, so the checklist would lie).
- **No Cancel button.** The adapter ignores the token (see Current state), and a "stop
  waiting" that leaves the roll running would still save a result behind the user's back.
  Worst case is ~3 × the model's latency; the timer makes that visible. If a real cancel is
  ever needed it is an LlmTornado streaming-path change, not a UI change.

### Result panel (fixes Codex Web#5)

After success the page does **not** navigate:

```
┌ "Invoice per customer" saved ──────────────────────────────────┐
│ ⚠ 2 unresolved bindings:                                        │
│   • Control 'label3' (band 'Detail'): [Foo] does not resolve…   │
│ Saved as "Invoice per customer (AI)" — the original has manual  │
│   designer edits and was left untouched.   (only when it applies)│
│                        [Open in designer]   [Close]             │
└────────────────────────────────────────────────────────────────┘
```

- "Open in designer" navigates to `/designer?report=<SavedAs>` — always a fresh page load,
  so the same-name-reload problem of an embedded designer does not arise.
- "Close" returns to the list (refreshed) with the form contents retained.

### Designer `/designer?report=<name>`

```
┌ ← Reports   Invoice per customer                                        ┐
│ ┌ DxReportDesigner ────────────────────────────────────────────────────┐ │
```

- App bar: back link and name only. `DxReportDesigner` unchanged (`AllowMDI=false`).
- No `report` → `Nav.NavigateTo("/", replace: true)`. Unknown `report` → error line + back
  link (checked with `Find` before rendering the designer).
- The designer's own Save / Save As dialogs are untouched; Save As becomes create-only
  through the storage change below, so it can no longer overwrite another report.

### Error states

- DB unavailable on load: banner with the message and a **Retry** button (re-runs
  `ListNames`); New/Modify/Delete disabled until it succeeds.
- A run that throws: the error line inside the progress panel, form contents kept.
- Circuit lost: Blazor's default reconnect overlay. If the circuit survives, the run
  resumes and its result panel appears; if the circuit is gone, the page reloads and the list
  is re-read — a run that was in flight either saved (its row is in the list) or did not. No
  banner, no server-side job tracking (single user, ~15 s runs). *(As built: no banner.)*

### `<dialog>` usage

`<dialog open>` bound to a `bool` is **non-modal**: the page behind stays interactive and
Escape does nothing. Acceptable for the delete confirm because the page is also `Busy`-locked
during the delete; the dialog has an explicit Cancel button and closes on `@onkeydown`
Escape. No `showModal()`, so still no JS.

### Layout / styling

- `wwwroot/app.css` replaces the `<style>` block: CSS variables for the palette (keep the
  existing `#2c3e50` bar so both pages read as one app), card grid
  (`repeat(auto-fill, minmax(280px, 1fr))`), panel, badge, dialog, progress, `.status/.issues/.error`.
- No JS beyond what Blazor and the DX designer already ship.

## Components (all under `Components/`)

| File | Responsibility |
|---|---|
| `Pages/Home.razor` | list + badges pass + filter + New panel; owns `Busy` |
| `Pages/Designer.razor` | app bar + `DxReportDesigner`; redirect / not-found |
| `Shared/ReportCard.razor` | one card: name, badge, Open/Modify/Delete |
| `Shared/AiRunPanel.razor` | Generate-or-Modify form → progress → result |
| `Shared/ConfirmDialog.razor` | `<dialog>` with message + Confirm/Cancel |
| `Layout/MainLayout.razor` | top bar: title + model dropdown (moves out of Home) |

Service changes:

- `ReportDataV2Store.Delete(name)` — `DELETE … WHERE DisplayName=@n AND NOT IsPredefined`,
  returns rows affected.
- `ReportDataV2Store.ListNames()` → `List()` returning `(Name, IsPredefined)`.
- `ReportDataV2Storage.SetNewData` → `store.Insert` (create-only). An existing destination
  now surfaces as the designer's save error instead of an overwrite.
- `AIReportService.ModifyAsync` **rechecks before saving** (closes the race Codex found in
  v2: Modify running on A while A is opened, edited and saved in the designer or WinForms).
  The overwrite-vs-copy decision is made before the LLM roll; the write afterwards is
  **conditional and atomic**: `ReportDataV2Store.SaveIfUnchanged(name, layout, expectedBytes)`
  runs `UPDATE … SET Content=@layout WHERE DisplayName=@n AND Content=@expected AND NOT
  IsPredefined` with the bytes loaded at the start. Zero rows updated means the stored layout
  moved under us (designer, WinForms, second tab): insert the result as the `<name> (AI)` copy
  and say so in the result panel. No separate re-read, no window between check and write
  (Codex's v3 objection). Predefined rows can never match because the guard is in the
  statement.
- `AIReportService`: the selected model is already passed per call; nothing changes.
- No pipeline change.

## Flows

1. **Generate:** New panel → name checked (`IsValidName` + `Exists`) before the LLM call →
   progress → result → Open in designer or Close.
2. **Modify:** AI card → Modify → inline panel → progress → result (with the "(AI) copy"
   notice when the stored layout had manual edits) → Open or Close.
3. **Delete:** card → Delete → confirm → row gone, list refreshed; error line if the store
   refused (predefined) or the DB failed.
4. **Designer:** Open → designer page; ← Reports goes back. Save As onto an existing name is
   refused by the storage.
5. **DB down:** banner + Retry; actions disabled.

## Acceptance (Playwright, C#, against `dotnet run` of the web host)

- Home lists every non-null `DisplayName`; AI-generated ones get the badge after load;
  predefined rows have no Modify/Delete; filter narrows.
- Generate with an existing name or a name containing `/` shows the error without an LLM
  call (status text only, no navigation, form retained).
- Generate (real LLM, gpt-5.4-mini) shows the progress panel with a ticking timer and ends
  on the result panel; "Open in designer" lands on `/designer?report=<name>` with the
  designer rendered; "Close" returns to the list with the new card present.
- Modify on a report with manual edits ends with the "(AI)" notice and Open lands on the copy.
- Delete removes the row and the card; a second browser tab's list shows the removal after
  its own refresh.
- Race: start Modify on A, open A in a second tab, save a change there before the roll
  finishes → the Modify result lands as "A (AI)", A keeps the designer's save, the result
  panel says why. Self-check line: `SaveIfUnchanged` with stale expected bytes returns
  false and leaves the row untouched.
- Designer Save As onto an existing name is refused (DX error dialog), the existing report
  unchanged.
- `/designer` without a report ends on `/`; `/designer?report=nope` shows the error + back link.
- One screenshot pass: the app bar and the designer read as one app.

## Estimate

| Piece | Hours |
|---|---|
| Store: `Delete`, `List`, create-only `SetNewData`; Modify pre-save recheck + self-check lines | 1 |
| `app.css`, layout, top bar with model | 1 |
| `ReportCard`, gallery, badge pass, filter, delete + confirm | 2 |
| `AiRunPanel` (form, progress with timer, result) | 2 |
| Designer app bar + redirect + not-found | 0.5 |
| Playwright smoke (existing C# harness) | 1.5 |
| **Total** | **~8.5** |

Codex judged v1's 7.5 h credible only for the visual shell; the two items it priced at
12–16 h (designer-side Modify, real cancellation) are out of scope in v2, so the total stays
in the same band with the risk removed.

## Owner answers (2026-09-09)

1. D1 — **DevExpress.Blazor where possible.**
2. **Model dropdown in the top bar.**
3. **Modify only from the home page is acceptable.**

## As built (deviations from the text above)

- List is a `DxGrid` with the search box, not cards; actions are buttons in a template column.
- The Modify panel renders above the grid (same place as the New panel), not under the card.
- The New panel stays mounted but hidden after Close, so its text survives; a Modify panel
  is keyed by report name and discarded on Close.
- No "check the list" banner after a lost circuit (see Error states).
- `poc/store-check.cs` (DB-backed) covers Insert/SaveIfUnchanged/Delete; `translator-check`
  stays DB-free.
- Cosmetic: the designer page still shows a page scrollbar on some viewports (DX designer's
  own layout height); not chased.

## Review history

- v1 (2026-09-09) → Codex design review: factual corrections (non-modal `<dialog>`,
  server-side filtering, no "validating" status, bar colour attribution), two High design
  defects (in-designer Modify vs unsaved edits; Cancel impossible with the installed
  adapter), Medium: same-name reload, eager blob loading, Save As not create-only. Decisions
  D1/D2/D4/D5 agreed, D3 rejected as specified. Estimate: shell only.
- v2: D3 replaced (home-only Modify), Cancel and the staged progress checklist dropped,
  list renders before badges, "edited" badge dropped, `SetNewData` made create-only,
  redirect/not-found and error states specified, acceptance list extended.
- v2 → Codex: two wording fixes (retry example, reconnect outcome is unknown) and one
  remaining data-loss race (Modify in flight vs. a designer save of the same report).
- v3: `Busy` also locks Open; `ModifyAsync` re-reads the stored layout before saving and
  diverts to the "(AI)" copy if it changed; wording fixed; race added to acceptance.
- v3 → Codex: the re-read and the write are still two operations — a save can land between
  them. Proposed a conditional `UPDATE … WHERE Content=@original`.
- v4: adopted verbatim as `SaveIfUnchanged`. Ready to implement once the owner answers the
  three open questions.
- v5: owner answers recorded (DevExpress.Blazor, model in top bar, home-only Modify).
- Implementation 322a97f → Codex: no overwrite defect; Medium: Close lost typed input,
  badge pass outlived the page, timer mutation off the sync context; store check missing.
  268eb53 fixes all four.
