#:package LlmTornado@*
#:package LlmTornado.Microsoft.Extensions.AI@*
#:package Npgsql@8.0.7
#:package DevExpress.Reporting.Core@26.1.3
#:project ../XafAIReportDesigner/XafAIReportDesigner.Module/XafAIReportDesigner.Module.csproj
#:property PublishAot=false

// PoC: own AI report pipeline — LLM (any provider via LLMTornado) fills a report-spec
// JSON we design; a deterministic translator builds the XtraReport using the binding
// rules proven against DevExpress 26.1. Benchmark target: the invoice master-detail
// case, on gpt-5.4-mini — the model that fails DevExpress's own CTP workflow.

using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using DevExpress.DataAccess.ConnectionParameters;
using DevExpress.XtraReports.UI;
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

var specInstructions = """
You produce a REPORT SPECIFICATION as strict JSON (no markdown fences, no commentary).

JSON shape:
{
  "title": string,
  "masterView": string,              // the view the report iterates (one page/section per row)
  "pagePerMasterRow": bool,
  "masterFields": [ {"expression": string, "label": string|null, "format": string|null} ],
  "levels": [                        // nested one-to-many drill-downs, outermost first
    {
      "relation": string,            // EXACTLY ONE relation name, valid from the previous level's entity (or from masterView for the first level)
      "headerFields": [ {"expression": string, "label": string|null, "format": string|null} ],  // shown once per row of THIS level
      "columns": [ {"expression": string, "header": string, "format": string|null, "rightAlign": bool} ]  // table over THIS level's rows; usually only the innermost level has columns
    }
  ],
  "totals": [ {"label": string, "expression": string, "format": string|null} ]   // rendered after the innermost table, once per master row
}

Expression rules (DevExpress criteria language):
- masterFields may use ONLY columns of the master view, e.g. [InvoiceNumber], or functions over them: FormatString('{0:d}', [InvoiceDate]).
- headerFields/columns of a level use that level's entity columns, and lookups via relation names: [OrdersCustomers].[CompanyName].
- Computed columns may use + - * / and parentheses, e.g. [Quantity] * [UnitPrice] * (1 - [Discount] / 100).
- totals use aggregates over the innermost level's rows: Sum(expr), Count(), Avg(expr); literals allowed, e.g. Sum(...) * 0.21.
- Use ONLY columns and relation names that exist in the schema below. Never invent fields.
- Never show raw ID/uuid columns. Do not repeat the same information in masterFields and headerFields.
- format values are FormatString placeholders such as "{0:n2}" or "{0:d}" (or null).
""";

var stopwatch = Stopwatch.StartNew();
var api = new TornadoApi(new List<ProviderAuthentication>
{
    new ProviderAuthentication(LLmProviders.OpenAi, apiKey),
});
IChatClient chatClient = api.AsChatClient(model);

Console.WriteLine($"[1] Model: {model} (via LLMTornado). Requesting spec…");
ReportSpec? spec = null;
string rawResponse = "";
for (int attempt = 1; attempt <= 2 && spec == null; attempt++)
{
    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, specInstructions + "\n\nDATABASE SCHEMA:\n" + schemaText),
        new(ChatRole.User, userPrompt +
            (attempt > 1 ? "\n\nYour previous response was not valid JSON for the required shape. Output ONLY the JSON object." : "")),
    };
    var response = await chatClient.GetResponseAsync(messages);
    rawResponse = response.Text.Trim();
    if (rawResponse.StartsWith("```"))
        rawResponse = rawResponse.Trim('`').Replace("json\n", "").Trim();
    try
    {
        spec = JsonSerializer.Deserialize<ReportSpec>(rawResponse,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"    attempt {attempt}: JSON parse failed ({ex.Message})");
    }
}
if (spec == null) { Console.WriteLine("spec generation failed:\n" + rawResponse); return; }
var llmSeconds = stopwatch.Elapsed.TotalSeconds;
Console.WriteLine($"[2] Spec received in {llmSeconds:F1}s: master={spec.MasterView}, levels=[{string.Join(" > ", spec.Levels.Select(l => l.Relation))}], totals={spec.Totals.Count}");
File.WriteAllText(Path.Combine(scratch, "poc-spec.json"), rawResponse);

// ---- Deterministic translation: spec -> XtraReport ----
stopwatch.Restart();
var b = new NpgsqlConnectionStringBuilder(connString);
var report = BuildReport(spec, schemaService.Schema,
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

// ---------------------------------------------------------------------------

static XtraReport BuildReport(ReportSpec spec, SchemaInfo schema, DataConnectionParametersBase connectionParams)
{
    const float PageWidth = 650f;
    var report = new XtraReport
    {
        DataSource = SchemaSqlDataSourceFactory.Create(schema, "XafAIReportDesigner", connectionParams),
        DataMember = spec.MasterView,
    };

    // Title once per report.
    var header = new ReportHeaderBand { HeightF = 40 };
    header.Controls.Add(new XRLabel
    {
        Text = spec.Title, WidthF = PageWidth, HeightF = 30,
        Font = new Font("Segoe UI", 16, FontStyle.Bold),
    });
    report.Bands.Add(header);

    // Master fields plus upper-level headerFields (prefixed with their relation path),
    // once per master row. Lookup chains resolve fine from the root band; only the
    // DEEPEST level becomes a DetailReportBand — the proven DX shape. An intermediate
    // band with a prefix of the deep band's path breaks the deep band's iteration.
    var relations = SchemaSqlDataSourceFactory.BuildRelationMap(schema);
    var columns = SchemaSqlDataSourceFactory.BuildColumnMap(schema);

    var rootFields = new List<FieldSpec>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var field in spec.MasterFields)
    {
        var repaired = field with { Expression = RepairChains(field.Expression, spec.MasterView, relations, columns) };
        if (seen.Add(repaired.Expression)) rootFields.Add(repaired);
    }
    var prefix = new List<string>();
    foreach (var level in spec.Levels.Take(Math.Max(0, spec.Levels.Count - 1)))
    {
        prefix.Add(level.Relation);
        foreach (var field in level.HeaderFields)
        {
            var prefixed = PrefixChains(field.Expression, prefix);
            prefixed = RepairChains(prefixed, spec.MasterView, relations, columns);
            if (seen.Add(prefixed))
                rootFields.Add(field with { Expression = prefixed });
        }
    }

    var rootDetail = new DetailBand { HeightF = rootFields.Count * 22 + 8 };
    float y = 4;
    foreach (var field in rootFields)
    {
        var label = new XRLabel { WidthF = PageWidth, HeightF = 20, TopF = y, Font = new Font("Segoe UI", 9.75f) };
        label.ExpressionBindings.Add(new ExpressionBinding("Text", WithLabel(field)));
        rootDetail.Controls.Add(label);
        y += 22;
    }
    report.Bands.Add(rootDetail);

    // Single deep band with the full absolute path.
    DetailReportBand? lastBand = null;
    var deepEntity = spec.MasterView;
    foreach (var level in spec.Levels)
        if (relations.TryGetValue((deepEntity, level.Relation), out var target)) deepEntity = target;

    if (spec.Levels.Count > 0)
    {
        var deepest = spec.Levels[^1];
        var path = spec.MasterView + "." + string.Join(".", spec.Levels.Select(l => l.Relation));
        // Explicit DataSource on the band (DX-generated layouts do this) — without it the
        // detail prints only the first row even though footer aggregates see all rows.
        var band = new DetailReportBand
        {
            Name = "level_" + deepest.Relation,
            DataSource = report.DataSource,
            DataMember = path,
        };

        var repairedColumns = deepest.Columns
            .Select(c => c with { Expression = RepairChains(c.Expression, deepEntity, relations, columns) })
            .ToList();
        var detail = new DetailBand { HeightF = repairedColumns.Count > 0 ? 24 : 0 };
        if (repairedColumns.Count > 0)
        {
            var columnsHeader = new GroupHeaderBand { HeightF = 26, Name = "hdr_" + deepest.Relation };
            columnsHeader.Controls.Add(BuildRow(repairedColumns, PageWidth, isHeader: true));
            band.Bands.Add(columnsHeader);
            detail.Controls.Add(BuildRow(repairedColumns, PageWidth, isHeader: false));
        }
        band.Bands.Add(detail);
        report.Bands.Add(band);
        lastBand = band;
    }

    // Totals: group footer of the innermost level (proven per-master-row pattern).
    if (spec.Totals.Count > 0 && lastBand != null)
    {
        var footer = new GroupFooterBand { HeightF = spec.Totals.Count * 22 + 8 };
        float ty = 4;
        for (int i = 0; i < spec.Totals.Count; i++)
        {
            var total = spec.Totals[i];
            var isLast = i == spec.Totals.Count - 1;
            footer.Controls.Add(new XRLabel
            {
                Text = total.Label, WidthF = 200, HeightF = 20, TopF = ty, LeftF = PageWidth - 360,
                TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleRight,
                Font = new Font("Segoe UI", 9.75f, isLast ? FontStyle.Bold : FontStyle.Regular),
            });
            // Aggregates must be the TOP-LEVEL expression (a FormatString wrapper defeats
            // summary evaluation) — format goes in TextFormatString, the proven DX shape.
            var value = new XRLabel
            {
                WidthF = 160, HeightF = 20, TopF = ty, LeftF = PageWidth - 160,
                TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleRight,
                Font = new Font("Segoe UI", 9.75f, isLast ? FontStyle.Bold : FontStyle.Regular),
                TextFormatString = NormalizeFormat(total.Format) ?? "{0:n2}",
            };
            var aggregate = System.Text.RegularExpressions.Regex.Replace(
                total.Expression, @"\bsum(Sum|Count|Avg|Min|Max)\b", "$1");
            aggregate = RepairChains(aggregate, deepEntity, relations, columns);
            value.ExpressionBindings.Add(new ExpressionBinding("Text", aggregate));
            footer.Controls.Add(value);
            ty += 22;
        }
        lastBand.Bands.Add(footer);
    }

    if (spec.PagePerMasterRow && lastBand != null)
        lastBand.PageBreak = DevExpress.XtraReports.UI.PageBreak.AfterBand;

    return report;
}

static XRTable BuildRow(List<ColumnSpec> columns, float pageWidth, bool isHeader)
{
    var table = new XRTable { WidthF = pageWidth, HeightF = isHeader ? 24 : 22 };
    var row = new XRTableRow();
    table.Rows.Add(row);
    // First column gets double weight (usually the description).
    float unit = pageWidth / (columns.Count + 1);
    for (int i = 0; i < columns.Count; i++)
    {
        var column = columns[i];
        var cell = new XRTableCell
        {
            WidthF = i == 0 ? unit * 2 : unit,
            Font = new Font("Segoe UI", 9.75f, isHeader ? FontStyle.Bold : FontStyle.Regular),
            TextAlignment = column.RightAlign && !isHeader
                ? DevExpress.XtraPrinting.TextAlignment.MiddleRight
                : isHeader && column.RightAlign
                    ? DevExpress.XtraPrinting.TextAlignment.MiddleRight
                    : DevExpress.XtraPrinting.TextAlignment.MiddleLeft,
            Padding = new DevExpress.XtraPrinting.PaddingInfo(4, 4, 2, 2),
        };
        if (isHeader)
        {
            cell.Text = column.Header;
            cell.BackColor = Color.WhiteSmoke;
        }
        else
        {
            cell.ExpressionBindings.Add(new ExpressionBinding("Text", Formatted(column.Expression, column.Format)));
        }
        row.Cells.Add(cell);
    }
    return table;
}

static string WithLabel(FieldSpec field)
{
    var expr = Formatted(field.Expression, field.Format);
    return string.IsNullOrEmpty(field.Label) ? expr : $"'{field.Label}: ' + {expr}";
}

// Models emit bare .NET formats ("c2", "n2") as often as placeholders — normalize.
static string? NormalizeFormat(string? format) =>
    string.IsNullOrEmpty(format) ? null : format.Contains("{0") ? format : "{0:" + format + "}";

static string Formatted(string expression, string? format) =>
    NormalizeFormat(format) is string f ? $"FormatString('{f}', {expression})" : expression;

/// <summary>Prefixes every bracketed field chain in an expression with relation segments.</summary>
static string PrefixChains(string expression, List<string> prefixSegments)
{
    var prefixText = string.Concat(prefixSegments.Select(s => $"[{s}]."));
    return System.Text.RegularExpressions.Regex.Replace(expression,
        @"\[([A-Za-z_][A-Za-z0-9_]*)\](?:\s*\.\s*\[[A-Za-z0-9_]+\])*",
        m => prefixText + m.Value);
}

/// <summary>
/// Deterministic repair: for every field chain that does not resolve from the given
/// context entity, BFS the relation graph for the shortest prefix path that makes it
/// resolve, and prepend it. The LLM supplies intent; this guarantees correctness.
/// </summary>
static string RepairChains(string expression, string contextEntity,
    Dictionary<(string, string), string> relations, Dictionary<string, HashSet<string>> columns)
{
    return System.Text.RegularExpressions.Regex.Replace(expression,
        @"\[([A-Za-z_][A-Za-z0-9_]*)\](?:\s*\.\s*\[[A-Za-z0-9_]+\])*",
        m =>
        {
            var segments = m.Value.Replace("[", "").Replace("]", "").Split('.').Select(s => s.Trim()).ToArray();
            if (Resolves(contextEntity, segments)) return m.Value;

            // Over-qualified chain: drop redundant leading segments if the rest resolves.
            for (int k = 1; k < segments.Length; k++)
                if (Resolves(contextEntity, segments[k..]))
                    return string.Join(".", segments[k..].Select(s => $"[{s}]"));

            // BFS for the shortest relation path whose end entity resolves the chain.
            var queue = new Queue<(string Entity, List<string> Path)>();
            var visited = new HashSet<string> { contextEntity };
            queue.Enqueue((contextEntity, new List<string>()));
            while (queue.Count > 0)
            {
                var (entity, pathSoFar) = queue.Dequeue();
                foreach (var ((master, relationName), target) in relations.Select(kv => (kv.Key, kv.Value)))
                {
                    if (master != entity || !visited.Add(target)) continue;
                    var candidate = new List<string>(pathSoFar) { relationName };
                    if (candidate.Count > 3) continue;
                    if (Resolves(target, segments))
                        return string.Concat(candidate.Select(s => $"[{s}].")) + m.Value;
                    queue.Enqueue((target, candidate));
                }
            }
            return m.Value; // unresolvable — validation will flag it
        });

    bool Resolves(string entity, string[] segments)
    {
        for (int i = 0; i < segments.Length; i++)
        {
            var isLast = i == segments.Length - 1;
            if (relations.TryGetValue((entity, segments[i]), out var next)) { entity = next; continue; }
            if (isLast && columns.TryGetValue(entity, out var cols) && cols.Contains(segments[i])) return true;
            return false;
        }
        return true;
    }
}

record ReportSpec(string Title, string MasterView, bool PagePerMasterRow,
    List<FieldSpec> MasterFields, List<LevelSpec> Levels, List<TotalSpec> Totals);
record FieldSpec(string Expression, string? Label, string? Format);
record LevelSpec(string Relation, List<FieldSpec> HeaderFields, List<ColumnSpec> Columns);
record ColumnSpec(string Expression, string Header, string? Format, bool RightAlign);
record TotalSpec(string Label, string Expression, string? Format);
