using System.IO;
using System.Text;
using DevExpress.DataAccess.ConnectionParameters;
using DevExpress.DataAccess.Sql;
using DevExpress.DataAccess.Wizard.Model;
using DevExpress.DataAccess.Wizard.Services;
using DevExpress.XtraBars;
using DevExpress.XtraBars.Ribbon;
using DevExpress.XtraReports.UI;
using DevExpress.XtraReports.UserDesigner;
using LlmTornado;
using LlmTornado.Code;
using LlmTornado.Microsoft.Extensions.AI;
using Microsoft.Extensions.AI;
using Npgsql;
using XafAIReportDesigner.Module.Services;

namespace XafAIReportDesigner.ReportDesigner;

/// <summary>
/// Standalone report designer form. AI generation and modification run through the own
/// provider-agnostic pipeline (Database ribbon → Generate from Prompt / Modify via AI):
/// the LLM fills a report-spec JSON, <see cref="ReportSpecTranslator"/> builds the layout.
/// </summary>
public sealed class AIReportDesignerForm : XRDesignRibbonForm
{
    private const string AppConnectionName = "XafAIReportDesigner";

    private readonly string _connectionString;
    private readonly ReportDataV2Store _store;
    private readonly ReflectionSchemaDiscoveryService _schemaService;
    private readonly string _apiKey;
    private readonly string _defaultGenerateModel;
    private BarEditItem? _modelItem;

    public AIReportDesignerForm(string connectionString, ReflectionSchemaDiscoveryService schemaService,
        string apiKey, string defaultGenerateModel)
    {
        _connectionString = connectionString;
        _store = new ReportDataV2Store(connectionString);
        _schemaService = schemaService;
        _apiKey = apiKey;
        _defaultGenerateModel = defaultGenerateModel;

        Text = "AI Report Designer";
        WindowState = FormWindowState.Maximized;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var mdiController = DesignMdiController;
        if (mdiController != null)
        {
            // The Data Source Wizard's connection list only reads the app config file, so the
            // connection registered via DefaultConnectionStringProvider (preview/runtime
            // resolution) never shows up there — expose it through this wizard-side service.
            // The same service restores credentials on load: layouts store the connection
            // name only (saving strips passwords, which broke reloads from ReportDataV2).
            var connectionService = new AppConnectionStorageService(_connectionString);
            mdiController.RemoveService(typeof(IConnectionStorageService));
            mdiController.AddService(typeof(IConnectionStorageService), connectionService);
            mdiController.RemoveService(typeof(IConnectionProviderService));
            mdiController.AddService(typeof(IConnectionProviderService), connectionService);
        }

        AddDatabaseMenuItems();
    }

    private void AddDatabaseMenuItems()
    {
        var ribbon = RibbonControl;
        if (ribbon == null) return;

        // Add a "Database" ribbon page with Load/Save items.
        var page = new RibbonPage("Database");
        var group = new RibbonPageGroup("Reports");

        var loadItem = new BarButtonItem(ribbon.Manager, "Load from DB");
        loadItem.ItemClick += OnLoadFromDatabase;

        var saveItem = new BarButtonItem(ribbon.Manager, "Save to DB");
        saveItem.ItemClick += OnSaveToDatabase;

        group.ItemLinks.Add(loadItem);
        group.ItemLinks.Add(saveItem);
        page.Groups.Add(group);

        // Own AI pipeline (provider-agnostic via LLMTornado): the model fills a report-spec
        // JSON, the deterministic ReportSpecTranslator builds the layout. Any model works —
        // unlike the DX CTP behaviors above, which require gpt-5.2 and minutes per roll.
        var aiGroup = new RibbonPageGroup("AI");
        var generateItem = new BarButtonItem(ribbon.Manager, "Generate from Prompt");
        generateItem.ItemClick += OnGenerateFromPrompt;
        aiGroup.ItemLinks.Add(generateItem);

        // Spec-level modification: the LLM edits the report's embedded spec JSON, the
        // translator rebuilds — structural edits (move a column) are just array edits,
        // so the DX chat's "claimed success, no change" failure mode cannot occur.
        var modifyItem = new BarButtonItem(ribbon.Manager, "Modify via AI");
        modifyItem.ItemClick += OnModifyViaAI;
        aiGroup.ItemLinks.Add(modifyItem);

        var modelCombo = new DevExpress.XtraEditors.Repository.RepositoryItemComboBox();
        modelCombo.Items.AddRange(new[] { "gpt-5.4-mini", "gpt-5.6-luna", "gpt-5.6-terra", "gpt-5.2" });
        ribbon.Manager.RepositoryItems.Add(modelCombo);
        _modelItem = new BarEditItem(ribbon.Manager)
        {
            Caption = "Model",
            Edit = modelCombo,
            EditValue = _defaultGenerateModel,
            EditWidth = 120,
        };
        aiGroup.ItemLinks.Add(_modelItem);
        page.Groups.Add(aiGroup);

        ribbon.Pages.Add(page);
    }

    private async void OnGenerateFromPrompt(object? sender, ItemClickEventArgs e)
    {
        var prompt = PromptForText("Generate Report from Prompt",
            "Describe the report (the AI receives the full schema incl. relationships):");
        if (string.IsNullOrWhiteSpace(prompt)) return;

        var schemaText = _schemaService.GenerateDataSourceSchema() + "\n" +
            SchemaSqlDataSourceFactory.DescribeDataMembers(_schemaService.Schema);
        await RunSpecPipelineAsync("AI Report Generation",
            ReportSpecTranslator.BuildSystemPrompt(schemaText), prompt, prompt);
    }

    private async void OnModifyViaAI(object? sender, ItemClickEventArgs e)
    {
        var current = ActiveDesignPanel?.Report;
        if (current == null)
        {
            MessageBox.Show("No active report to modify.", "Modify via AI",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var currentSpec = ReportSpecTranslator.TryGetSpec(current);
        if (currentSpec == null)
        {
            MessageBox.Show(
                "This report has no embedded AI spec — only reports created via Generate from Prompt " +
                "(or previously modified here) can be modified this way.",
                "Modify via AI", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Modify rebuilds from the spec; hand edits made in the designer are not in the spec
        // (RPT-015). The result opens as a NEW document, so nothing is lost unless the user
        // saves over the original — but say so before spending the roll.
        if (ReportSpecTranslator.HasManualEdits(current) && MessageBox.Show(
                "This report was edited in the designer after it was generated. Modify via AI " +
                "rebuilds it from the AI spec, so those manual edits will not carry over.\n\n" +
                "The result opens as a new document; this one stays open. Continue?",
                "Modify via AI", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        var change = PromptForText("Modify Report via AI",
            "Describe the change (e.g. \"move the quantity column to the first position\"):");
        if (string.IsNullOrWhiteSpace(change)) return;

        var schemaText = _schemaService.GenerateDataSourceSchema() + "\n" +
            SchemaSqlDataSourceFactory.DescribeDataMembers(_schemaService.Schema);
        current.Extensions.TryGetValue(ReportSpecTranslator.PromptExtensionKey, out var originalPrompt);
        var newReport = await RunSpecPipelineAsync("Modify via AI",
            ReportSpecTranslator.BuildModifySystemPrompt(schemaText, currentSpec), change,
            (originalPrompt ?? "") + "\n[modified]: " + change);
        if (newReport != null) newReport.DisplayName = current.DisplayName;
    }

    /// <summary>
    /// Shared own-pipeline loop: up to 3 LLM rolls, translate + validate each, keep the
    /// best, embed the winning spec in the report, open it in the designer.
    /// </summary>
    private async Task<XtraReport?> RunSpecPipelineAsync(string title, string systemPrompt,
        string userPrompt, string promptToEmbed)
    {
        var model = _modelItem?.EditValue as string is { Length: > 0 } m ? m : _defaultGenerateModel;

        using var statusForm = new Form
        {
            Text = title,
            Size = new System.Drawing.Size(480, 120),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ControlBox = false,
        };
        var statusLabel = new Label { Dock = DockStyle.Fill, Padding = new Padding(10), Text = "Starting…" };
        statusForm.Controls.Add(statusLabel);
        statusForm.Show(this);

        var started = DateTime.Now;
        void SetStatus(string s) => statusLabel.Text = $"[{DateTime.Now - started:mm\\:ss}] {s}";

        try
        {
            var api = new TornadoApi(new List<ProviderAuthentication>
            {
                new ProviderAuthentication(LLmProviders.OpenAi, _apiKey),
            });
            IChatClient chatClient = api.AsChatClient(model);

            var result = await SpecPipeline.RollBestAsync(chatClient, _schemaService.Schema,
                systemPrompt, userPrompt, promptToEmbed,
                AppConnectionName, BuildConnectionParameters(_connectionString), SetStatus);
            var best = result.Report;
            var bestIssues = result.Issues;

            if (best == null)
                throw new InvalidOperationException($"{model} did not return a valid report spec after 3 attempts.");

            if (bestIssues is { Count: > 0 })
            {
                MessageBox.Show(
                    "The generated report has unresolved bindings you may want to fix in the designer:\n\n- " +
                    string.Join("\n- ", bestIssues.Take(12)) +
                    (bestIssues.Count > 12 ? $"\n… and {bestIssues.Count - 12} more" : ""),
                    title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            OpenReport(best);
            return best;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Operation failed:\n{ex.Message}", title,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            statusForm.Close();
        }
    }

    private string? PromptForText(string title, string caption)
    {
        using var dialog = new Form
        {
            Text = title,
            Size = new System.Drawing.Size(520, 260),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };
        var label = new Label { Text = caption, Dock = DockStyle.Top, Height = 30, Padding = new Padding(5) };
        var textBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
        var okButton = new Button { Text = "Generate", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(textBox);
        dialog.Controls.Add(label);
        dialog.Controls.Add(okButton);
        return dialog.ShowDialog(this) == DialogResult.OK ? textBox.Text : null;
    }

    private void OnLoadFromDatabase(object? sender, ItemClickEventArgs e)
    {
        try
        {
            // Names only — the layout blob is fetched for the chosen row (RPT-016).
            var names = _store.ListNames();
            if (names.Count == 0)
            {
                MessageBox.Show("No reports found in the database.", "Load Report",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Show a simple selection dialog.
            using var dialog = new Form
            {
                Text = "Load Report from Database",
                Size = new System.Drawing.Size(400, 350),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
            };

            var listBox = new ListBox { Dock = DockStyle.Fill };
            foreach (var name in names) listBox.Items.Add(name);
            if (listBox.Items.Count > 0) listBox.SelectedIndex = 0;

            var okButton = new Button { Text = "Load", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
            dialog.Controls.Add(listBox);
            dialog.Controls.Add(okButton);
            dialog.AcceptButton = okButton;

            if (dialog.ShowDialog(this) == DialogResult.OK && listBox.SelectedIndex >= 0)
            {
                var selectedName = names[listBox.SelectedIndex];
                if (_store.Load(selectedName) is { Length: > 0 } content)
                {
                    var report = new XtraReport();
                    using var stream = new MemoryStream(content);
                    report.LoadLayoutFromXml(stream);
                    // The row's name wins over whatever the layout carries (layouts saved
                    // before RPT-011 may still hold the name they were originally saved as).
                    report.DisplayName = selectedName;
                    RestoreAppConnection(report);
                    OpenReport(report);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading report:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSaveToDatabase(object? sender, ItemClickEventArgs e)
    {
        try
        {
            var panel = ActiveDesignPanel;
            var report = panel?.Report;
            if (report == null)
            {
                MessageBox.Show("No active report to save.", "Save Report",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Prompt for report name using a simple input dialog.
            var reportName = PromptForReportName(report.DisplayName ?? "New Report");
            if (string.IsNullOrWhiteSpace(reportName)) return;
            reportName = reportName.Trim();
            if (!ReportDataV2Store.IsValidName(reportName))
            {
                MessageBox.Show("Report name may not contain '/' or '\\' (it doubles as the web designer's URL).",
                    "Save Report", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_store.Find(reportName).IsPredefined)
            {
                MessageBox.Show($"'{reportName}' is a predefined XAF report and cannot be overwritten — choose another name.",
                    "Save Report", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The layout carries its own name: without this, "Save as B" leaves DisplayName at
            // "A" and the next Save silently overwrites A (RPT-011).
            report.DisplayName = reportName;
            using var stream = new MemoryStream();
            report.SaveLayoutToXml(stream);
            var content = stream.ToArray();

            _store.Save(reportName, content, ExtractDataTypeName(report));
            // Documented DX pattern for custom saving: otherwise closing still prompts to
            // save to a file (XRDesignPanel.ReportState docs).
            panel!.ReportState = ReportState.Saved;
            MessageBox.Show($"Report '{reportName}' saved successfully.", "Save Report",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving report:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string? PromptForReportName(string defaultName)
    {
        using var dialog = new Form
        {
            Text = "Save Report",
            Size = new System.Drawing.Size(350, 150),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };

        var label = new Label { Text = "Report name:", Dock = DockStyle.Top, Height = 25, Padding = new Padding(5) };
        var textBox = new TextBox { Text = defaultName, Dock = DockStyle.Top };
        var okButton = new Button { Text = "Save", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };

        dialog.Controls.Add(textBox);
        dialog.Controls.Add(label);
        dialog.Controls.Add(okButton);
        dialog.AcceptButton = okButton;

        return dialog.ShowDialog() == DialogResult.OK ? textBox.Text : null;
    }

    /// <summary>
    /// XAF reads <c>ReportDataV2.DataTypeName</c> as a CLR type name (the business object the
    /// report is "about"). The root DataMember names the master view; map it back to the
    /// entity's CLR type, or leave the association empty for anything else (RPT-014).
    /// </summary>
    private string ExtractDataTypeName(XtraReport report)
    {
        var root = (report.DataMember ?? "").Split('.')[0];
        var entity = _schemaService.Schema.Entities.FirstOrDefault(e => e.TableName == root);
        return entity?.ClrType.FullName ?? "";
    }

    /// <summary>
    /// Saving a report strips credentials from serialized connection parameters, and
    /// IConnectionProviderService is only consulted for name-only connections — so for
    /// loaded layouts, reassign the full parameters on every app-named data source directly.
    /// </summary>
    private void RestoreAppConnection(XtraReport report)
    {
        // DataSourceManager, not ComponentStorage — the latter misses data sources
        // referenced only by DetailReportBands (own-pipeline reports have one).
        foreach (var sqlDs in DevExpress.XtraReports.DataSourceManager
                     .GetDataSources(report, includeSubReports: true).OfType<SqlDataSource>())
        {
            if (sqlDs.ConnectionName == AppConnectionName)
                sqlDs.ConnectionParameters = BuildConnectionParameters(_connectionString);
        }
    }

    private static PostgreSqlConnectionParameters BuildConnectionParameters(string npgsqlConnectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(npgsqlConnectionString);
        return new PostgreSqlConnectionParameters(b.Host, b.Port, b.Database, b.Username, b.Password);
    }

    /// <summary>
    /// Supplies the app's PostgreSQL connection to the Data Source Wizard's
    /// "existing connections" list. Name matches the DefaultConnectionStringProvider
    /// registration in Program.cs so saved reports resolve at preview time.
    /// </summary>
    private sealed class AppConnectionStorageService : IConnectionStorageService, IConnectionProviderService
    {
        private readonly SqlDataConnection _connection;

        public AppConnectionStorageService(string npgsqlConnectionString)
        {
            _connection = new SqlDataConnection(
                AppConnectionName,
                BuildConnectionParameters(npgsqlConnectionString))
            {
                // Serialize only the name into report layouts; LoadConnection restores
                // the full parameters (saved layouts never carry credentials).
                StoreConnectionNameOnly = true,
            };
        }

        public bool CanSaveConnection => false;
        public bool Contains(string connectionName) => connectionName == _connection.Name;
        public IEnumerable<SqlDataConnection> GetConnections() { yield return _connection; }
        public void SaveConnection(string connectionName, IDataConnection dataConnection, bool saveCredentials) { }

        public SqlDataConnection? LoadConnection(string connectionName)
            => connectionName == _connection.Name ? _connection : null;
    }
}
