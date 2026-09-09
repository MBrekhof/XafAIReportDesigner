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
        return await RunAsync(ReportSpecTranslator.BuildSystemPrompt(schemaText), prompt, prompt,
            model, reportName, store, createNew: true, setStatus);
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

        // Modify rebuilds from the spec. If the layout was edited in the designer since, those
        // edits are not in the spec — keep the edited report and save the result beside it
        // instead of overwriting (RPT-015).
        var edited = ReportSpecTranslator.HasManualEdits(current);
        var targetName = edited ? NextFreeName(store, reportName + " (AI)") : reportName;
        var result = await RunAsync(ReportSpecTranslator.BuildModifySystemPrompt(schemaText, currentSpec), change,
            (originalPrompt ?? "") + "\n[modified]: " + change, model, targetName, store, createNew: edited, setStatus);
        return edited && result.Success
            ? result with { Message = $"'{reportName}' has manual designer edits and was left untouched — {result.Message}" }
            : result;
    }

    private static string NextFreeName(ReportDataV2Store store, string baseName)
    {
        var name = baseName;
        for (int i = 2; store.Exists(name); i++) name = $"{baseName} {i}";
        return name;
    }

    private async Task<AIReportResult> RunAsync(string systemPrompt, string userPrompt, string promptToEmbed,
        string model, string reportName, ReportDataV2Store store, bool createNew, Action<string>? setStatus)
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
        report.DisplayName = reportName;
        using var stream = new MemoryStream();
        report.SaveLayoutToXml(stream);
        // Generate inserts (unique index turns a lost race into an error, never an overwrite);
        // Modify updates in place.
        if (createNew) store.Insert(reportName, stream.ToArray());
        else store.Save(reportName, stream.ToArray());

        var issues = result.Issues ?? [];
        return new AIReportResult(true,
            issues.Count == 0 ? $"'{reportName}' saved." : $"'{reportName}' saved with {issues.Count} unresolved binding(s).",
            issues, reportName);
    }

    private string SchemaText() =>
        schemaService.GenerateDataSourceSchema() + "\n" +
        SchemaSqlDataSourceFactory.DescribeDataMembers(schemaService.Schema);
}
