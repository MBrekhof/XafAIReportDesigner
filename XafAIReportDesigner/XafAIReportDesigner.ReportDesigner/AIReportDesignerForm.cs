using System.ComponentModel;
using System.IO;
using System.Text;
using DevExpress.AIIntegration.Reporting;
using DevExpress.AIIntegration.WinForms.Reporting;
using DevExpress.Utils.Behaviors;
using DevExpress.XtraBars;
using DevExpress.XtraBars.Ribbon;
using DevExpress.XtraReports.UI;
using DevExpress.XtraReports.UserDesigner;
using Microsoft.EntityFrameworkCore;

namespace XafAIReportDesigner.ReportDesigner;

/// <summary>
/// Standalone report designer form with AI Prompt-to-Report behavior.
/// Extends <see cref="XRDesignRibbonForm"/> and attaches the behavior
/// using the documented <c>Attach&lt;T&gt;</c> API.
/// </summary>
public sealed class AIReportDesignerForm : XRDesignRibbonForm
{
    private readonly IContainer _components;
    private readonly BehaviorManager _behaviorManager;
    private readonly string _connectionString;
    private readonly string _schemaPrompt;

    public AIReportDesignerForm(string connectionString, string schemaPrompt)
    {
        _connectionString = connectionString;
        _schemaPrompt = schemaPrompt;
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
            _behaviorManager.Attach<ReportPromptToReportBehavior>(mdiController, behavior =>
            {
                behavior.Properties.RetryAttemptCount = 3;
                behavior.Properties.FixLayoutErrors = true;

                // Build schema-aware predefined prompts so the AI knows the actual database structure.
                behavior.Properties.PredefinedPrompts = BuildPredefinedPrompts();
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
        ribbon.Pages.Add(page);
    }

    private AIReportPromptCollection BuildPredefinedPrompts()
    {
        var collection = AIReportPromptCollection.GetDefaultReportPrompts();

        // Embed the full schema context so the AI uses real table/column names.
        collection.Add(new AIReportPrompt
        {
            Title = "Order Summary Report",
            Text = $"""
                {_schemaPrompt}

                Create a report showing all orders with:
                - Group by Customer (CompanyName)
                - Columns: OrderDate, ShipName, ShipCity, Freight
                - Sort by OrderDate descending
                - Summary: total Freight per customer and grand total
                - Use the connection named "XafAIReportDesigner"
                """,
        });

        collection.Add(new AIReportPrompt
        {
            Title = "Product Catalog Report",
            Text = $"""
                {_schemaPrompt}

                Create a product catalog report with:
                - Group by Category (CategoryName)
                - Columns: ProductName, QuantityPerUnit, UnitPrice, UnitsInStock
                - Sort by ProductName within each category
                - Summary: count of products per category, average UnitPrice
                - Use the connection named "XafAIReportDesigner"
                """,
        });

        collection.Add(new AIReportPrompt
        {
            Title = "Invoice Report",
            Text = $"""
                {_schemaPrompt}

                Create an invoice report with:
                - Group by Customer (CompanyName)
                - Columns: InvoiceDate, Amount, ShipName, ShipCity
                - Sort by InvoiceDate descending
                - Summary: total Amount per customer and grand total
                - Use the connection named "XafAIReportDesigner"
                """,
        });

        collection.Add(new AIReportPrompt
        {
            Title = "Custom Report (with schema context)",
            Text = $"""
                {_schemaPrompt}

                Use the connection named "XafAIReportDesigner".
                Create a report for: [describe your report here]
                """,
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
