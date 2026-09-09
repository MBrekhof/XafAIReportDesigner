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
                  new ColumnSpec("[OrdersOrderItems].[Quantity]", "Qty", null, true)])],
    Totals: [new TotalSpec("Total", "Sum([Quantity] * [UnitPrice])", "c2")]);

var report = ReportSpecTranslator.BuildReport(spec, schema, "X", conn);
var expressions = AllExpressions(report).ToList();

Assert(expressions.Contains("'Customer''s ref: ' + [InvoiceNumber]"), "apostrophe in label is doubled", expressions);
Assert(expressions.Contains("FormatString('{0:n2}', [OrderItemsProducts].[UnitPrice])"),
    "wrong-direction chain repaired to the PRODUCT price, not stripped to the line price", expressions);
Assert(expressions.Contains("[Quantity]"), "over-qualified chain still stripped", expressions);
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
