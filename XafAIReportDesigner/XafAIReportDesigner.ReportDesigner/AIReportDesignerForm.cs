using System.ComponentModel;
using System.IO;
using System.Text;
using DevExpress.AIIntegration;
using DevExpress.AIIntegration.Reporting;
using DevExpress.AIIntegration.Reporting.Common.Extensions;
using DevExpress.AIIntegration.WinForms.Reporting;
using DevExpress.DataAccess.ConnectionParameters;
using DevExpress.DataAccess.Sql;
using DevExpress.DataAccess.Wizard.Model;
using DevExpress.DataAccess.Wizard.Services;
using DevExpress.Utils.Behaviors;
using DevExpress.XtraBars;
using DevExpress.XtraBars.Ribbon;
using DevExpress.XtraReports.UI;
using DevExpress.XtraReports.UserDesigner;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using XafAIReportDesigner.Module.Services;

namespace XafAIReportDesigner.ReportDesigner;

/// <summary>
/// Standalone report designer form with AI Prompt-to-Report behavior.
/// Extends <see cref="XRDesignRibbonForm"/> and attaches the behavior
/// using the documented <c>Attach&lt;T&gt;</c> API.
/// </summary>
public sealed class AIReportDesignerForm : XRDesignRibbonForm
{
    private const string AppConnectionName = "XafAIReportDesigner";

    private readonly IContainer _components;
    private readonly BehaviorManager _behaviorManager;
    private readonly string _connectionString;
    private readonly ReflectionSchemaDiscoveryService _schemaService;

    public AIReportDesignerForm(string connectionString, ReflectionSchemaDiscoveryService schemaService)
    {
        _connectionString = connectionString;
        _schemaService = schemaService;
        _components = new Container();
        _behaviorManager = new BehaviorManager(_components);

        Text = "AI Report Designer";
        WindowState = FormWindowState.Maximized;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Attach AI Prompt-to-Report behavior to the MDI controller.
        // Using OnLoad to ensure the designer is fully initialized.
        var mdiController = DesignMdiController;
        if (mdiController != null)
        {
            // The wizard's connection list only reads the app config file, so the
            // connection registered via DefaultConnectionStringProvider (preview/runtime
            // resolution) never shows up there — expose it through this wizard-side service.
            // The same service restores credentials on load: layouts store the connection
            // name only (saving strips passwords, which broke reloads from ReportDataV2).
            var connectionService = new AppConnectionStorageService(_connectionString);
            mdiController.RemoveService(typeof(IConnectionStorageService));
            mdiController.AddService(typeof(IConnectionStorageService), connectionService);
            mdiController.RemoveService(typeof(IConnectionProviderService));
            mdiController.AddService(typeof(IConnectionProviderService), connectionService);

            _behaviorManager.Attach<ReportPromptToReportBehavior>(mdiController, behavior =>
            {
                behavior.Properties.RetryAttemptCount = 3;
                behavior.Properties.FixLayoutErrors = true;
                // GPT-5-series models reject temperature values other than 1 (DX docs warning).
                behavior.Properties.Temperature = 1f;

                // Build schema-aware predefined prompts so the AI knows the actual database structure.
                behavior.Properties.PredefinedPrompts = BuildPredefinedPrompts();
            });

            // AI Assistant chat panel: edit the open report layout in natural language (CTP).
            _behaviorManager.Attach<ReportModifyBehavior>(mdiController, behavior =>
            {
                behavior.Properties.FixLayoutErrors = true;
                behavior.Properties.RetryAttemptCount = 3;
                behavior.Properties.Temperature = 1f;
            });

            System.Diagnostics.Debug.WriteLine("[AIReportDesignerForm] ReportPromptToReportBehavior attached via Attach<T>");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[AIReportDesignerForm] DesignMdiController is NULL — cannot attach behavior");
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

        // Headless generation via the 26.1 cross-platform API — unlike the wizard,
        // this path feeds the AI our curated schema including FK relationships.
        var aiGroup = new RibbonPageGroup("AI");
        var generateItem = new BarButtonItem(ribbon.Manager, "Generate from Prompt");
        generateItem.ItemClick += OnGenerateFromPrompt;
        aiGroup.ItemLinks.Add(generateItem);
        page.Groups.Add(aiGroup);

        ribbon.Pages.Add(page);
    }

    private async void OnGenerateFromPrompt(object? sender, ItemClickEventArgs e)
    {
        var prompt = PromptForText("Generate Report from Prompt",
            "Describe the report (the AI receives the full schema incl. relationships):");
        if (string.IsNullOrWhiteSpace(prompt)) return;

        using var statusForm = new Form
        {
            Text = "AI Report Generation",
            Size = new System.Drawing.Size(480, 120),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ControlBox = false,
        };
        var statusLabel = new Label { Dock = DockStyle.Fill, Padding = new Padding(10), Text = "Starting…" };
        statusForm.Controls.Add(statusLabel);
        statusForm.Show(this);

        try
        {
            var schemaText = _schemaService.GenerateDataSourceSchema() + "\n" +
                SchemaSqlDataSourceFactory.DescribeDataMembers(_schemaService.Schema);
            var request = new PromptToReportRequest(prompt, schemaText)
            {
                ReportGenerationHost = new WinFormsAIReportGenerationHost(this, s => statusLabel.Text = s),
                FixLayoutErrors = true,
            };
            var report = await AIExtensionsContainerDesktop.Default.GeneratePromptToReportAsync(request);

            // The API generates layout + bindings only — attach the matching data source.
            SchemaSqlDataSourceFactory.Attach(report,
                _schemaService.Schema, AppConnectionName, BuildConnectionParameters(_connectionString));

            // Generation quality varies run to run (CTP): resolve every binding against the
            // schema graph; on failures, roll a fresh generation and keep the better result.
            // (A repair request updating the existing report regenerates broadly AND mutates
            // the passed instance — measured strictly worse than a fresh roll.)
            var issues = SchemaSqlDataSourceFactory.ValidateBindings(report, _schemaService.Schema);
            if (issues.Count > 0)
            {
                statusLabel.Text = $"{issues.Count} binding issue(s) — trying a fresh generation…";
                var retry = await AIExtensionsContainerDesktop.Default.GeneratePromptToReportAsync(
                    new PromptToReportRequest(prompt, schemaText)
                    {
                        ReportGenerationHost = new WinFormsAIReportGenerationHost(this, s => statusLabel.Text = s),
                        FixLayoutErrors = true,
                    });
                SchemaSqlDataSourceFactory.Attach(retry,
                    _schemaService.Schema, AppConnectionName, BuildConnectionParameters(_connectionString));
                var retryIssues = SchemaSqlDataSourceFactory.ValidateBindings(retry, _schemaService.Schema);
                if (retryIssues.Count < issues.Count)
                {
                    report = retry;
                    issues = retryIssues;
                }
            }

            if (issues.Count > 0)
            {
                MessageBox.Show(
                    "The generated report has unresolved bindings you may want to fix in the designer:\n\n- " +
                    string.Join("\n- ", issues.Take(12)) +
                    (issues.Count > 12 ? $"\n… and {issues.Count - 12} more" : ""),
                    "AI Report Generation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            OpenReport(report);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Report generation failed:\n{ex.Message}", "AI Report Generation",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    /// <summary>
    /// Routes generation-workflow callbacks to the UI thread: clarification questions
    /// become dialogs, progress updates land on the status label.
    /// </summary>
    private sealed class WinFormsAIReportGenerationHost : IAIReportGenerationHost
    {
        private readonly Control _owner;
        private readonly Action<string> _setStatus;

        public WinFormsAIReportGenerationHost(Control owner, Action<string> setStatus)
        {
            _owner = owner;
            _setStatus = setStatus;
        }

        public Task<PromptClarificationAnswer> ClarifyPromptAsync(PromptClarificationQuestion request)
        {
            var answer = (PromptClarificationAnswer)_owner.Invoke(() => ShowClarificationDialog(request));
            return Task.FromResult(answer);
        }

        public void NotifyAsync(string status, string reasoning)
        {
            if (_owner.IsHandleCreated)
                _owner.BeginInvoke(() => _setStatus(status));
        }

        private PromptClarificationAnswer ShowClarificationDialog(PromptClarificationQuestion request)
        {
            using var dialog = new Form
            {
                Text = "AI Assistant — Clarification",
                Size = new System.Drawing.Size(520, 320),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
            };
            var label = new Label { Text = request.Text, Dock = DockStyle.Top, Height = 80, Padding = new Padding(5), AutoEllipsis = true };
            var okButton = new Button { Text = "Answer", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
            dialog.Controls.Add(label);
            dialog.AcceptButton = okButton;

            Func<string> getAnswer;
            if (request.Choices is { Count: > 0 })
            {
                var listBox = new ListBox { Dock = DockStyle.Fill };
                foreach (var choice in request.Choices) listBox.Items.Add(choice);
                listBox.SelectedIndex = 0;
                dialog.Controls.Add(listBox);
                listBox.BringToFront();
                getAnswer = () => listBox.SelectedItem?.ToString() ?? "";
            }
            else
            {
                var textBox = new TextBox { Multiline = true, Dock = DockStyle.Fill };
                dialog.Controls.Add(textBox);
                textBox.BringToFront();
                getAnswer = () => textBox.Text;
            }
            dialog.Controls.Add(okButton);

            return dialog.ShowDialog(_owner.FindForm()) == DialogResult.OK
                ? PromptClarificationAnswer.FromValue(getAnswer())
                : PromptClarificationAnswer.Canceled();
        }
    }

    private static AIReportPromptCollection BuildPredefinedPrompts()
    {
        // Intent-only templates: the 26.1 wizard's "Add Data Source" step attaches
        // the data source structure to the LLM prompt itself, and the connection is
        // picked in the wizard UI — no schema text or connection hints needed here.
        var collection = AIReportPromptCollection.GetDefaultReportPrompts();

        collection.Add(new AIReportPrompt
        {
            Title = "Order Summary Report",
            Text = "Create an order summary report grouped by customer company name " +
                   "with order date, ship name, ship city, and freight columns, sorted by " +
                   "order date descending. Show total freight per customer and a grand total.",
        });

        collection.Add(new AIReportPrompt
        {
            Title = "Product Catalog Report",
            Text = "Create a product catalog report grouped by category name with product name, " +
                   "quantity per unit, unit price, and units in stock columns, sorted by product name " +
                   "within each category. Show product count and average unit price per category.",
        });

        collection.Add(new AIReportPrompt
        {
            Title = "Invoice Report",
            Text = "Create an invoice report grouped by customer company name with invoice date, " +
                   "amount, ship name, and ship city columns, sorted by invoice date descending. " +
                   "Show total amount per customer and a grand total.",
        });

        return collection;
    }

    private void OnLoadFromDatabase(object? sender, ItemClickEventArgs e)
    {
        try
        {
            using var context = CreateDbContext();
            var reports = context.Set<DevExpress.Persistent.BaseImpl.EF.ReportDataV2>()
                .OrderBy(r => r.DisplayName)
                .ToList();

            if (reports.Count == 0)
            {
                MessageBox.Show("No reports found in the database.", "Load Report",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Show a simple selection dialog.
            var names = reports.Select(r => r.DisplayName ?? "(unnamed)").ToArray();
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
                var selectedReport = reports[listBox.SelectedIndex];
                if (selectedReport.Content is { Length: > 0 })
                {
                    var report = new XtraReport();
                    using var stream = new MemoryStream(selectedReport.Content);
                    report.LoadLayoutFromXml(stream);
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
            var report = ActiveDesignPanel?.Report;
            if (report == null)
            {
                MessageBox.Show("No active report to save.", "Save Report",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Prompt for report name using a simple input dialog.
            var reportName = PromptForReportName(report.DisplayName ?? "New Report");
            if (string.IsNullOrWhiteSpace(reportName)) return;

            using var stream = new MemoryStream();
            report.SaveLayoutToXml(stream);
            var content = stream.ToArray();

            using var context = CreateDbContext();
            var existing = context.Set<DevExpress.Persistent.BaseImpl.EF.ReportDataV2>()
                .FirstOrDefault(r => r.DisplayName == reportName);

            // Extract metadata that XAF expects on ReportDataV2.
            var dataTypeName = ExtractDataTypeName(report);

            if (existing != null)
            {
                existing.Content = content;
                existing.DataTypeName = dataTypeName;
            }
            else
            {
                var reportData = new DevExpress.Persistent.BaseImpl.EF.ReportDataV2
                {
                    DisplayName = reportName,
                    Content = content,
                    DataTypeName = dataTypeName,
                    IsInplaceReport = false,
                };
                context.Set<DevExpress.Persistent.BaseImpl.EF.ReportDataV2>().Add(reportData);
            }

            context.SaveChanges();
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
    /// Tries to extract a data type name from the report's data source
    /// so XAF can associate the report with a business object type.
    /// Returns the SQL data source's first query name or empty string.
    /// </summary>
    private static string ExtractDataTypeName(XtraReport report)
    {
        // Check for SqlDataSource — the AI wizard typically creates these.
        if (report.DataSource is DevExpress.DataAccess.Sql.SqlDataSource sqlDs)
        {
            var firstQuery = sqlDs.Queries.OfType<DevExpress.DataAccess.Sql.SelectQuery>().FirstOrDefault();
            if (firstQuery != null)
                return firstQuery.Name;

            // Fall back to any query name.
            var anyQuery = sqlDs.Queries.Cast<DevExpress.DataAccess.Sql.SqlQuery>().FirstOrDefault();
            if (anyQuery != null)
                return anyQuery.Name;
        }

        // Check DataMember as fallback.
        if (!string.IsNullOrWhiteSpace(report.DataMember))
            return report.DataMember;

        return "";
    }

    /// <summary>
    /// Saving a report strips credentials from serialized connection parameters, and
    /// IConnectionProviderService is only consulted for name-only connections — so for
    /// loaded layouts, reassign the full parameters on every app-named data source directly.
    /// </summary>
    private void RestoreAppConnection(XtraReport report)
    {
        foreach (var sqlDs in report.ComponentStorage.OfType<SqlDataSource>())
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

    private DbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new ReportDbContext(options);
    }

    /// <summary>
    /// Lightweight DbContext that only maps <see cref="DevExpress.Persistent.BaseImpl.EF.ReportDataV2"/>
    /// to avoid XAF's change-tracking requirements on entities like FileData.
    /// </summary>
    private sealed class ReportDbContext : DbContext
    {
        public ReportDbContext(DbContextOptions<ReportDbContext> options) : base(options) { }

        public DbSet<DevExpress.Persistent.BaseImpl.EF.ReportDataV2> ReportDataV2 => Set<DevExpress.Persistent.BaseImpl.EF.ReportDataV2>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Map only ReportDataV2 and its base type (BaseObject provides ID).
            modelBuilder.Entity<DevExpress.Persistent.BaseImpl.EF.ReportDataV2>(entity =>
            {
                entity.ToTable("ReportDataV2");
                entity.HasKey(e => e.ID);
            });

            // Ignore all other XAF base types that EF might try to discover.
            modelBuilder.Ignore<DevExpress.Persistent.BaseImpl.EF.BaseObject>();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _behaviorManager?.Dispose();
            _components?.Dispose();
        }
        base.Dispose(disposing);
    }
}
