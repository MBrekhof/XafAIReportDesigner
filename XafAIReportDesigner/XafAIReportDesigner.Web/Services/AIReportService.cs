using DevExpress.DataAccess.ConnectionParameters;
using DevExpress.XtraReports.UI;
using LlmTornado;
using LlmTornado.Code;
using LlmTornado.Microsoft.Extensions.AI;
using Microsoft.Extensions.AI;
using Npgsql;
using XafAIReportDesigner.Module.Services;

namespace XafAIReportDesigner.Web.Services;

public record AIReportResult(bool Success, string Message, IReadOnlyList<string> Issues);

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
        var schemaText = SchemaText();
        return await RunAsync(ReportSpecTranslator.BuildSystemPrompt(schemaText), prompt, prompt,
            model, reportName, store, setStatus);
    }

    public async Task<AIReportResult> ModifyAsync(string reportName, string change, string model,
        ReportDataV2Store store, Action<string>? setStatus = null)
    {
        var layout = store.Load(reportName);
        if (layout == null)
            return new AIReportResult(false, $"Report '{reportName}' not found.", []);

        var current = new XtraReport();
        using (var stream = new MemoryStream(layout)) current.LoadLayoutFromXml(stream);
        var currentSpec = ReportSpecTranslator.TryGetSpec(current);
        if (currentSpec == null)
            return new AIReportResult(false,
                $"'{reportName}' has no embedded AI spec — only AI-generated reports can be modified this way.", []);

        current.Extensions.TryGetValue(ReportSpecTranslator.PromptExtensionKey, out var originalPrompt);
        var schemaText = SchemaText();
        return await RunAsync(ReportSpecTranslator.BuildModifySystemPrompt(schemaText, currentSpec), change,
            (originalPrompt ?? "") + "\n[modified]: " + change, model, reportName, store, setStatus);
    }

    private async Task<AIReportResult> RunAsync(string systemPrompt, string userPrompt, string promptToEmbed,
        string model, string reportName, ReportDataV2Store store, Action<string>? setStatus)
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

        result.Report.DisplayName = reportName;
        using var stream = new MemoryStream();
        result.Report.SaveLayoutToXml(stream);
        store.Save(reportName, stream.ToArray());

        var issues = result.Issues ?? [];
        return new AIReportResult(true,
            issues.Count == 0 ? $"'{reportName}' saved." : $"'{reportName}' saved with {issues.Count} unresolved binding(s).",
            issues);
    }

    private string SchemaText() =>
        schemaService.GenerateDataSourceSchema() + "\n" +
        SchemaSqlDataSourceFactory.DescribeDataMembers(schemaService.Schema);
}
