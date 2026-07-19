# Done

#### RPT-006: Polish the headless generation path (ID: 1061)

Completed 2026-07-19. Binding validation (`ValidateBindings` resolves every expression path and
band DataMember against the schema/relation graph) + fresh-retry keep-best (repair-in-place
measured worse: regenerates broadly AND mutates the passed report), elapsed-time/roll status,
page-break + one-hop-per-band binding rules, Discount semantics fix verified. User click-through
of Generate from Prompt done (slow but working; 'overview' report's 2 validation errors were
real AI mistakes, repaired in place — two-hop band path and hallucinated column). Model
benchmark: gpt-5.2 is the only model completing the DX CTP workflow (2.5–4.5 min/roll);
gpt-5.4-mini and gpt-5.6 luna/terra break, refuse, or 400.

#### RPT-004: Wire ReflectionSchemaDiscoveryService into the 26.1 cross-platform API (ID: 1059)

Completed 2026-07-19, branch `rpt-004-headless-generation`. **The master-detail case the wizard
failed is solved**: headless generation produced a 21-page invoice run — one invoice per page,
each with its own customer, dates, line items and per-invoice totals (verified by rendering
against live PostgreSQL data; result saved to ReportDataV2 as "RPT004-Invoice").

The working recipe (all doc-verified/empirically tested):

- `PromptToReportRequest.DataSourceSchema` is a plain **string**. `GenerateDataSourceSchema()`
  (Module) emits factual PostgreSQL schema text — tables, columns, enum values, and the FK graph.
- **The API generates layout + bindings only — no data source component.** `SchemaSqlDataSourceFactory`
  (Module) builds the matching `SqlDataSource`: one query per entity + `MasterDetailInfo` relations
  for every FK in both directions (one-to-many "InvoicesOrders", lookup "OrdersCustomers";
  self-referencing FKs skipped). `DescribeDataMembers()` tells the AI the exact binding rules:
  root DataMember = master view, DetailReportBand paths are absolute relation-name paths
  ("Invoices.InvoicesOrders.OrdersOrderItems"), expressions reach related rows via relation names.
- **Gotcha:** assigning `XtraReport.DataSource` resets band DataMembers set without a source —
  `SchemaSqlDataSourceFactory.Attach()` snapshots members, attaches, reassigns (normalizing
  relative paths to absolute).
- `IAIReportGenerationHost` works: the multi-agent flow asked a real clarification question
  (page size, with choices) and resumed on answer. App has `WinFormsAIReportGenerationHost`
  (dialogs + status label) behind Database → AI → "Generate from Prompt"; the scratch console
  host auto-answers.
- Discovery fix: computed get-only properties (Employee.FullName) are skipped — they have no
  DB column and broke query validation.

#### RPT-003: Add AI "Modify Report" chat behavior (ID: 1058)

Completed 2026-07-19, branch `rpt-003-modify-report-chat`.

`ReportModifyBehavior` attached alongside the prompt-to-report behavior; project SDK switched to
`Microsoft.NET.Sdk.Razor` (chat panel is a Blazor WebView). Along the way: default model bumped
gpt-4o → gpt-5.2 with `Temperature = 1` on both behaviors (GPT-5 series rejects other values),
and saved-report reloads fixed (saving strips credentials from serialized connections;
`IConnectionProviderService` only covers name-only connections, so `RestoreAppConnection()`
reassigns full parameters after `LoadLayoutFromXml` — user-verified with FableOne).

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
