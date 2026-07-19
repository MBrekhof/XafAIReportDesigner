# Done

#### RPT-003: Add AI "Modify Report" chat behavior (ID: 1058)

Completed 2026-07-19, branch `rpt-003-modify-report-chat`.

`ReportModifyBehavior` attached alongside the prompt-to-report behavior; project SDK switched to
`Microsoft.NET.Sdk.Razor` (chat panel is a Blazor WebView). Along the way: default model bumped
gpt-4o → gpt-5.2 with `Temperature = 1` on both behaviors (GPT-5 series rejects other values),
and saved-report reloads fixed via `IConnectionProviderService` + `StoreConnectionNameOnly`
(saving strips credentials from serialized connections).

**User-verified working:** chat executes precise layout commands (bands, styling); wizard
generates data-bound reports (FableOne/FableTwo).
**Known CTP limits (upstream, not ours):** generation quality varies run to run (occasional
fieldless layouts); chat handles layout operations only — data-source requests fail with
"modification failed", and questions crash the JSON parser ("'X' is an invalid start of a
value"); master-detail (invoice) layouts bind wrong because the wizard's multi-query data
sources carry no relations. The relationship-aware fix is RPT-004's headless path.

#### RPT-001: Upgrade DevExpress 25.2.3 → 26.1 (ID: 1056)

Completed 2026-07-19, branch `rpt-001-devexpress-26.1`.

All four DevExpress packages bumped to 26.1.3. `Microsoft.Extensions.AI.OpenAI` moved `9.*` → `10.*`
(the 9.* pin was already being lifted to 10.3.0 by the 26.1 dependency graph; transitive
`Microsoft.Extensions.AI` = 10.5.1, matching the docs). EF Core 8.0.18 / Npgsql 8.x unchanged —
exactly what 26.1.3's `Persistent.BaseImpl.EFCore` targets. Build clean; designer smoke-ran
(ribbon, custom Database tab, Design Analyzer 0 errors); the 26.1 multi-agent AI wizard confirmed
live (new "Select a Data Source Option" page, AI Prompt-to-Report entry). Note: clarification
questions only appear during generation when a prompt is ambiguous — not a fixed wizard step.

#### RPT-002: Remove schema-stuffed predefined prompts (ID: 1057)

Completed 2026-07-19, branch `rpt-001-devexpress-26.1`.

- Predefined prompts reduced to three intent-only templates (Order Summary, Product Catalog,
  Invoice); schema blob and `_schemaPrompt` plumbing deleted. 26.1's wizard attaches data-source
  metadata to the LLM prompt itself. `ReflectionSchemaDiscoveryService` retained for RPT-004.
- PromptAugmentation relocation (planned c) was N/A: the property doesn't exist on the WinForms
  prompt-to-report behavior in 26.1 (prompt-to-expression / WPF only), and the connection is picked
  in the wizard UI anyway — hint deleted outright.
- Real bug found + fixed: the wizard's existing-connections list reads only the app config file;
  the `DefaultConnectionStringProvider` registration (preview-time resolution) never fed it.
  Fixed with `AppConnectionStorageService : IConnectionStorageService` on the `DesignMdiController`.
- Infra: `xafaireportdesigner` DB was missing after the repo rename — created + seeded in the
  `xaf-postgres` container, which itself had to be recreated (same data volume) because its host
  port publish was silently inactive.
- User-verified in the running wizard: connection listed, tables show, slim prompts present.
