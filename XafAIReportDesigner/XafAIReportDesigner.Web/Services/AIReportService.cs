using DevExpress.DataAccess.ConnectionParameters;
using DevExpress.XtraReports.UI;
using LlmTornado;
using LlmTornado.Code;
using LlmTornado.Microsoft.Extensions.AI;
using Microsoft.Extensions.AI;
using Npgsql;
using XafAIReportDesigner.Module.Services;

namespace XafAIReportDesigner.Web.Services;

/// <summary>SavedAs is the name the layout landed under (differs from the request when Modify preserved an edited report).</summary>
public record AIReportResult(bool Success, string Message, IReadOnlyList<string> Issues, string? SavedAs = null);

/// <summary>Web front for the own pipeline: generate/modify a report and save it to ReportDataV2.</summary>
public sealed class AIReportService(
    ReflectionSchemaDiscoveryService schemaService, string connectionString, string apiKey, string defaultModel)
{
    private const string AppConnectionName = "XafAIReportDesigner";

    public string DefaultModel => defaultModel;
    public static readonly string[] KnownModels = ["gpt-5.4-mini", "gpt-5.6-luna", "gpt-5.6-terra", "gpt-5.2"];

    public async Task<AIReportResult> GenerateAsync(string prompt, string model, string reportName,
        ReportDataV2Store store, Action<string>? setStatus = null)
    {
        // Create-only (RPT-011): a new report must not silently replace an existing one, and
        // the name must survive as a designer URL. Checked before the LLM round-trip.
        if (!ReportDataV2Store.IsValidName(reportName))
            return new AIReportResult(false, "Report name may not be empty or contain '/' or '\\'.", []);
        if (store.Exists(reportName))
            return new AIReportResult(false, $"A report named '{reportName}' already exists — pick another name or use Modify.", []);

        var schemaText = SchemaText();
        return await RunAsync(ReportSpecTranslator.BuildSystemPrompt(schemaText), prompt, prompt, model,
            report => Persist(store, report, reportName, insert: true), setStatus);
    }

    public async Task<AIReportResult> ModifyAsync(string reportName, string change, string model,
        ReportDataV2Store store, Action<string>? setStatus = null)
    {
        var layout = store.Load(reportName);
        if (layout == null)
            return new AIReportResult(false, $"Report '{reportName}' not found.", []);

        using var current = new XtraReport();
        using (var stream = new MemoryStream(layout)) current.LoadLayoutFromXml(stream);
        var currentSpec = ReportSpecTranslator.TryGetSpec(current);
        if (currentSpec == null)
            return new AIReportResult(false,
                $"'{reportName}' has no embedded AI spec — only AI-generated reports can be modified this way.", []);

        current.Extensions.TryGetValue(ReportSpecTranslator.PromptExtensionKey, out var originalPrompt);
        var schemaText = SchemaText();

        // Modify rebuilds from the spec. Manual designer edits are not in the spec (RPT-015):
        // an edited report is left untouched and the result saved beside it. The overwrite
        // itself is a compare-and-save against the bytes loaded above (RPT-010): if anything
        // saved the report while the model was working, the result goes beside it too.
        var edited = ReportSpecTranslator.HasManualEdits(current);
        string? note = null;
        var result = await RunAsync(ReportSpecTranslator.BuildModifySystemPrompt(schemaText, currentSpec), change,
            (originalPrompt ?? "") + "\n[modified]: " + change, model, report =>
            {
                if (!edited && store.SaveIfUnchanged(reportName, Serialize(report, reportName), layout))
                    return reportName;
                var copy = NextFreeName(store, reportName, " (AI)");
                note = edited
                    ? $"'{reportName}' has manual designer edits and was left untouched — the result is saved as '{copy}'."
                    : $"'{reportName}' was saved by someone else while the AI was working and was left untouched — the result is saved as '{copy}'.";
                return Persist(store, report, copy, insert: true);
            }, setStatus);
        return note != null && result.Success ? result with { Message = note } : result;
    }

    private async Task<AIReportResult> RunAsync(string systemPrompt, string userPrompt, string promptToEmbed,
        string model, Func<XtraReport, string> persist, Action<string>? setStatus)
    {
        var api = new TornadoApi(new List<ProviderAuthentication>
        {
            new ProviderAuthentication(LLmProviders.OpenAi, apiKey),
        });
        IChatClient chatClient = api.AsChatClient(model);

        var b = new NpgsqlConnectionStringBuilder(connectionString);
        var result = await SpecPipeline.RollBestAsync(chatClient, schemaService.Schema,
            systemPrompt, userPrompt, promptToEmbed, AppConnectionName,
            new PostgreSqlConnectionParameters(b.Host, b.Port, b.Database, b.Username, b.Password), setStatus);

        if (result.Report == null)
            return new AIReportResult(false, $"{model} did not return a valid report spec after 3 attempts.", result.Issues ?? []);

        using var report = result.Report; // only the serialized layout outlives this call
        var savedAs = persist(report);

        var issues = result.Issues ?? [];
        return new AIReportResult(true,
            issues.Count == 0 ? $"'{savedAs}' saved." : $"'{savedAs}' saved with {issues.Count} unresolved binding(s).",
            issues, savedAs);
    }

    private static string Persist(ReportDataV2Store store, XtraReport report, string name, bool insert)
    {
        var bytes = Serialize(report, name);
        if (insert) store.Insert(name, bytes); else store.Save(name, bytes);
        return name;
    }

    private static byte[] Serialize(XtraReport report, string name)
    {
        report.DisplayName = name; // the layout carries its own name (RPT-011)
        using var stream = new MemoryStream();
        report.SaveLayoutToXml(stream);
        return stream.ToArray();
    }

    private static string NextFreeName(ReportDataV2Store store, string sourceName, string marker)
    {
        // Truncate the SOURCE so the marker and a counter always fit the 256-char DisplayName.
        const int MaxLength = 256, CounterRoom = 8;
        var room = MaxLength - marker.Length - CounterRoom;
        var stem = (sourceName.Length > room ? sourceName[..room] : sourceName) + marker;
        var name = stem;
        for (int i = 2; store.Exists(name); i++) name = $"{stem} {i}";
        return name;
    }

    private string SchemaText() =>
        schemaService.GenerateDataSourceSchema() + "\n" +
        SchemaSqlDataSourceFactory.DescribeDataMembers(schemaService.Schema);
}
