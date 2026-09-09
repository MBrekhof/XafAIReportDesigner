#:package DevExpress.Reporting.Core@26.1.3
#:project ../XafAIReportDesigner/XafAIReportDesigner.Module/XafAIReportDesigner.Module.csproj
#:property PublishAot=false

// Offline self-check for ReportSpecTranslator (RPT-012). No LLM, no database: builds a
// report from a hand-written spec and asserts the produced expression bindings.
// Run:  dotnet run poc/translator-check.cs        (from the repo root)

using DevExpress.DataAccess.ConnectionParameters;
using DevExpress.XtraReports.UI;
using XafAIReportDesigner.Module.Services;

var schemaService = new ReflectionSchemaDiscoveryService(
    typeof(XafAIReportDesigner.Module.Attributes.AIVisibleAttribute).Assembly);
var schema = schemaService.Schema;
var conn = new PostgreSqlConnectionParameters("localhost", 5432, "x", "x", "x");

// 1. Master-detail: apostrophe in a label, wrong-direction chain, format literal.
var spec = new ReportSpec("Check", "Invoices", PagePerMasterRow: true,
    MasterFields: [new FieldSpec("[InvoiceNumber]", "Customer's ref", null)],
    Levels: [new LevelSpec("InvoicesOrders", [new FieldSpec("[OrdersCustomers].[CompanyName]", "Customer", null)], []),
             new LevelSpec("OrdersOrderItems", [],
                 [new ColumnSpec("[ProductsOrderItems].[UnitPrice]", "List price", "n2", true),
                  new ColumnSpec("[OrdersOrderItems].[Quantity]", "Qty", null, true),
                  new ColumnSpec("[OrdersOrderItems].[OrderItemsProducts].[UnitPrice]", "List price 2", null, true)])],
    Totals: [new TotalSpec("Total", "Sum([Quantity] * [UnitPrice])", "c2")]);

var report = ReportSpecTranslator.BuildReport(spec, schema, "X", conn);
var expressions = AllExpressions(report).ToList();

Assert(expressions.Contains("'Customer''s ref: ' + [InvoiceNumber]"), "apostrophe in label is doubled", expressions);
Assert(expressions.Contains("FormatString('{0:n2}', [OrderItemsProducts].[UnitPrice])"),
    "wrong-direction chain repaired to the PRODUCT price, not stripped to the line price", expressions);
Assert(expressions.Contains("[Quantity]"), "over-qualified chain still stripped", expressions);
Assert(expressions.Contains("[OrderItemsProducts].[UnitPrice]"),
    "over-qualified chain keeps its explicit relation (not rewritten into a parent round trip)", expressions);
Assert(!expressions.Any(e => e.Contains("[OrderItemsOrders].[OrdersOrderItems]")),
    "no hop-by-hop rewrite into a collection lookup", expressions);
Assert(expressions.Contains("'Customer: ' + [InvoicesOrders].[OrdersCustomers].[CompanyName]"),
    "intermediate headerFields prefixed into the root band", expressions);
Assert(SchemaSqlDataSourceFactory.ValidateBindings(report, schema).Count == 0, "no binding issues", expressions);
var deep = report.Bands.OfType<DetailReportBand>().Single();
Assert(deep.PageBreak == PageBreak.AfterBand, "page break on the deep band", []);

// 2. Master-only report: PagePerMasterRow must land on the root Detail band.
var flat = new ReportSpec("Flat", "Customers", true, [new FieldSpec("[CompanyName]", null, null)], [], []);
var flatReport = ReportSpecTranslator.BuildReport(flat, schema, "X", conn);
Assert(flatReport.Bands.OfType<DetailBand>().Single().PageBreak == PageBreak.AfterBand,
    "page break on root Detail when there are no levels", []);

// 3. Format literal containing an apostrophe.
var fmt = new ReportSpec("Fmt", "Customers", false, [new FieldSpec("[CompanyName]", null, "{0}'s")], [], []);
var fmtExpr = AllExpressions(ReportSpecTranslator.BuildReport(fmt, schema, "X", conn)).Single();
Assert(fmtExpr == "FormatString('{0}''s', [CompanyName])", "apostrophe in format is doubled", [fmtExpr]);

// 4. RPT-013: ParseSpec shape guard and the validator's syntax gate.
Assert(ReportSpecTranslator.ParseSpec("{}") == null, "ParseSpec rejects {} (no masterView)", []);
Assert(ReportSpecTranslator.ParseSpec("not json") == null, "ParseSpec rejects non-JSON", []);
var partial = ReportSpecTranslator.ParseSpec("""{"masterView":"Customers"}""");
Assert(partial is { MasterFields.Count: 0, Levels.Count: 0, Totals.Count: 0 }, "ParseSpec fills missing collections", []);
var partialReport = ReportSpecTranslator.BuildReport(partial!, schema, "X", conn);
Assert(SchemaSqlDataSourceFactory.ValidateBindings(partialReport, schema).Count == 0, "partial spec still builds", []);
Assert(ReportSpecTranslator.ParseSpec("""{"masterView":"Customers","masterFields":[{"label":"Customer"}]}""") == null,
    "ParseSpec fails the roll on an entry without an expression (not silently dropped)", []);
var bare = new ReportSpec("Bare", "Customers", false, [new FieldSpec("Nope + 1", null, null)], [], []);
var bareIssues = SchemaSqlDataSourceFactory.ValidateBindings(ReportSpecTranslator.BuildReport(bare, schema, "X", conn), schema);
Assert(bareIssues.Any(i => i.Contains("'Nope' is not a column")), "unbracketed unknown field is caught via the parsed form", bareIssues);
var broken = new ReportSpec("Broken", "Customers", false, [new FieldSpec("[CompanyName] +", null, null)], [], []);
var brokenIssues = SchemaSqlDataSourceFactory.ValidateBindings(ReportSpecTranslator.BuildReport(broken, schema, "X", conn), schema);
Assert(brokenIssues.Any(i => i.Contains("does not parse")), "malformed expression is reported", brokenIssues);
var noMember = new XtraReport();
Assert(SchemaSqlDataSourceFactory.ValidateBindings(noMember, schema).Any(i => i.Contains("DataMember is empty")),
    "empty root DataMember is reported", []);

// 5. RPT-015: layout fingerprint survives a save/load round trip and notices a hand edit.
ReportSpecTranslator.AttachSpec(report, "{}", "p");
report.DisplayName = "renamed after attach"; // hosts do this; must not count as an edit
byte[] xml;
using (var ms = new MemoryStream()) { report.SaveLayoutToXml(ms); xml = ms.ToArray(); }
var reloaded = new XtraReport();
using (var ms = new MemoryStream(xml)) reloaded.LoadLayoutFromXml(ms);
Assert(!ReportSpecTranslator.HasManualEdits(reloaded), "no manual edits after save/load round trip", []);
reloaded.Bands.OfType<DetailBand>().First().Controls.Add(new XRPictureBox { Name = "logo", WidthF = 50, HeightF = 50 });
Assert(ReportSpecTranslator.HasManualEdits(reloaded), "added control is detected as a manual edit", []);
Assert(!ReportSpecTranslator.HasManualEdits(flatReport), "report without fingerprint (pre-RPT-015) reports no edits", []);

Console.WriteLine("translator-check: all assertions passed");
return 0;

static IEnumerable<string> AllExpressions(XtraReport report)
{
    foreach (var control in report.AllControls<XRControl>())
        foreach (ExpressionBinding binding in control.ExpressionBindings)
            yield return binding.Expression;
}

static void Assert(bool ok, string what, IEnumerable<string> context)
{
    if (ok) { Console.WriteLine($"  ok   {what}"); return; }
    Console.WriteLine($"  FAIL {what}\n       " + string.Join("\n       ", context));
    Environment.Exit(1);
}
