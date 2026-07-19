#nullable enable
using DevExpress.DataAccess.ConnectionParameters;
using DevExpress.XtraReports.UI;
using Microsoft.Extensions.AI;

namespace XafAIReportDesigner.Module.Services
{
    public record SpecPipelineResult(XtraReport? Report, string? SpecJson, IReadOnlyList<string>? Issues);

    /// <summary>
    /// Host-agnostic own-pipeline loop shared by the WinForms and Web designers:
    /// up to 3 LLM rolls, translate + validate each, keep the best, embed the
    /// winning spec in the report.
    /// </summary>
    public static class SpecPipeline
    {
        public static async Task<SpecPipelineResult> RollBestAsync(
            IChatClient chatClient, SchemaInfo schema, string systemPrompt, string userPrompt,
            string promptToEmbed, string connectionName, DataConnectionParametersBase connectionParameters,
            Action<string>? setStatus = null)
        {
            XtraReport? best = null;
            ReportSpec? bestSpec = null;
            IReadOnlyList<string>? bestIssues = null;
            var parseFailed = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                setStatus?.Invoke($"Attempt {attempt}: requesting report spec…");
                var response = await chatClient.GetResponseAsync(new List<ChatMessage>
                {
                    new(ChatRole.System, systemPrompt),
                    new(ChatRole.User, userPrompt + (parseFailed
                        ? "\n\nYour previous response was not valid JSON for the required shape. Output ONLY the JSON object."
                        : "")),
                });
                var spec = ReportSpecTranslator.ParseSpec(response.Text);
                if (spec == null) { parseFailed = true; continue; }

                setStatus?.Invoke($"Attempt {attempt}: translating spec…");
                var report = ReportSpecTranslator.BuildReport(spec, schema, connectionName, connectionParameters);
                var issues = SchemaSqlDataSourceFactory.ValidateBindings(report, schema);
                if (bestIssues == null || issues.Count < bestIssues.Count)
                {
                    best = report;
                    bestSpec = spec;
                    bestIssues = issues;
                }
                if (bestIssues.Count == 0) break;
            }

            if (best == null || bestSpec == null) return new SpecPipelineResult(null, null, bestIssues);

            var specJson = System.Text.Json.JsonSerializer.Serialize(bestSpec);
            ReportSpecTranslator.AttachSpec(best, specJson, promptToEmbed);
            return new SpecPipelineResult(best, specJson, bestIssues);
        }
    }
}
