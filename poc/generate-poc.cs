#:package LlmTornado@*
#:package LlmTornado.Microsoft.Extensions.AI@*
#:package Npgsql@8.0.7
#:package DevExpress.Reporting.Core@26.1.3
#:project ../XafAIReportDesigner/XafAIReportDesigner.Module/XafAIReportDesigner.Module.csproj
#:property PublishAot=false

// Harness for the own AI report pipeline (now productized): drives the Module's
// ReportSpecTranslator — the exact code behind the app's "Generate from Prompt" button —
// headlessly, renders a PDF for inspection, and saves the layout to ReportDataV2.
// Benchmark reference: gpt-5.4-mini completes in ~4s where the DX CTP pipeline needed
// 140-400s on gpt-5.2.

using System.Diagnostics;
using System.Text.Json;
using DevExpress.DataAccess.ConnectionParameters;
using LlmTornado;
using LlmTornado.Code;
using LlmTornado.Microsoft.Extensions.AI;
using Microsoft.Extensions.AI;
using Npgsql;
using XafAIReportDesigner.Module.Services;

var scratch = @"C:\Users\marti\AppData\Local\Temp\claude\C--projects-XafAIReportDesigner\77dafe7e-785d-4ecc-ae67-832a9096119f\scratchpad";
var connString = "Host=localhost;Port=5432;Database=xafaireportdesigner;Username=xaf;Password=xaf123";
var model = Environment.GetEnvironmentVariable("MODEL_OVERRIDE") ?? "gpt-5.4-mini";

var config = JsonDocument.Parse(File.ReadAllText(
    @"C:\projects\XafAIReportDesigner\XafAIReportDesigner\XafAIReportDesigner.ReportDesigner\appsettings.Development.json"));
var apiKey = config.RootElement.GetProperty("OpenAI").GetProperty("ApiKey").GetString()!;

var schemaService = new ReflectionSchemaDiscoveryService(
    typeof(XafAIReportDesigner.Module.Attributes.AIVisibleAttribute).Assembly);
var schemaText = schemaService.GenerateDataSourceSchema() + "\n" +
    SchemaSqlDataSourceFactory.DescribeDataMembers(schemaService.Schema);

var userPrompt =
    "Create an invoice report. One invoice per page. " +
    "Per invoice show invoice number, invoice date, due date, and the customer's company name, city and country. " +
    "Then a table with only that invoice's order items: product name, quantity, unit price, and line total with the discount applied. " +
    "At the bottom of each invoice: subtotal, 21% VAT, and grand total.";

var stopwatch = Stopwatch.StartNew();
var api = new TornadoApi(new List<ProviderAuthentication>
{
    new ProviderAuthentication(LLmProviders.OpenAi, apiKey),
});
IChatClient chatClient = api.AsChatClient(model);

Console.WriteLine($"[1] Model: {model} (via LLMTornado). Requesting spec…");
var systemPrompt = ReportSpecTranslator.BuildSystemPrompt(schemaText);
ReportSpec? spec = null;
string rawResponse = "";
for (int attempt = 1; attempt <= 2 && spec == null; attempt++)
{
    var response = await chatClient.GetResponseAsync(new List<ChatMessage>
    {
        new(ChatRole.System, systemPrompt),
        new(ChatRole.User, userPrompt +
            (attempt > 1 ? "\n\nYour previous response was not valid JSON for the required shape. Output ONLY the JSON object." : "")),
    });
    rawResponse = response.Text;
    spec = ReportSpecTranslator.ParseSpec(rawResponse);
    if (spec == null) Console.WriteLine($"    attempt {attempt}: JSON parse failed");
}
if (spec == null) { Console.WriteLine("spec generation failed:\n" + rawResponse); return; }
var llmSeconds = stopwatch.Elapsed.TotalSeconds;
Console.WriteLine($"[2] Spec received in {llmSeconds:F1}s: master={spec.MasterView}, levels=[{string.Join(" > ", spec.Levels.Select(l => l.Relation))}], totals={spec.Totals.Count}");
File.WriteAllText(Path.Combine(scratch, "poc-spec.json"), rawResponse);

// ---- Deterministic translation via the shared Module service ----
stopwatch.Restart();
var b = new NpgsqlConnectionStringBuilder(connString);
var report = ReportSpecTranslator.BuildReport(spec, schemaService.Schema, "XafAIReportDesigner",
    new PostgreSqlConnectionParameters(b.Host, b.Port, b.Database, b.Username, b.Password));

var issues = SchemaSqlDataSourceFactory.ValidateBindings(report, schemaService.Schema);
Console.WriteLine($"[3] Translated in {stopwatch.Elapsed.TotalSeconds:F1}s. Validation: {issues.Count} issue(s)");
foreach (var issue in issues) Console.WriteLine($"    - {issue}");

stopwatch.Restart();
report.CreateDocument();
Console.WriteLine($"[4] Rendered {report.Pages.Count} pages in {stopwatch.Elapsed.TotalSeconds:F1}s (DB has 20 invoices)");
report.ExportToPdf(Path.Combine(scratch, "poc-invoice.pdf"));

await using var conn = new NpgsqlConnection(connString);
await conn.OpenAsync();
await using (var del = new NpgsqlCommand("DELETE FROM \"ReportDataV2\" WHERE \"DisplayName\" = 'PoC-Invoice'", conn))
    await del.ExecuteNonQueryAsync();
using var ms = new MemoryStream();
report.SaveLayoutToXml(ms);
await using var cmd = new NpgsqlCommand(
    "INSERT INTO \"ReportDataV2\" (\"DisplayName\", \"Content\", \"DataTypeName\", \"IsInplaceReport\", \"IsPredefined\") " +
    "VALUES ('PoC-Invoice', @content, '', false, false)", conn);
cmd.Parameters.AddWithValue("content", ms.ToArray());
await cmd.ExecuteNonQueryAsync();
Console.WriteLine($"[5] Saved to ReportDataV2 as 'PoC-Invoice'. Total LLM time {llmSeconds:F1}s vs DevExpress pipeline 140-400s.");
