using DevExpress.DataAccess;
using Microsoft.Extensions.Configuration;
using XafAIReportDesigner.Module.Services;

namespace XafAIReportDesigner.ReportDesigner;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .Build();

        // API key for the own AI pipeline (LLMTornado — any provider/model).
        var apiKey = configuration["OpenAI:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                "OpenAI API key is not configured.\n\nSet the \"OpenAI:ApiKey\" value in appsettings.Development.json.",
                "Configuration Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Discover entities from the Module assembly for AI context.
        var moduleAssembly = typeof(XafAIReportDesigner.Module.Attributes.AIVisibleAttribute).Assembly;
        var schemaService = new ReflectionSchemaDiscoveryService(moduleAssembly);
        var schema = schemaService.Schema;

        System.Diagnostics.Debug.WriteLine($"[ReportDesigner] Discovered {schema.Entities.Count} entities:");
        foreach (var entity in schema.Entities)
        {
            System.Diagnostics.Debug.WriteLine($"  - {entity.Name} (table: {entity.TableName}) — {entity.Description}");
        }

        // Resolve database connection string from config.
        var connectionString = configuration["Database:ConnectionString"]
            ?? "Host=localhost;Port=5432;Database=xafaireportdesigner;Username=xaf;Password=xaf123";

        // Register the connection with XpoProvider prefix so the Report Data Source Wizard can find it.
        var xpoConnectionString = configuration["Database:XpoConnectionString"]
            ?? "XpoProvider=Postgres;Server=localhost;Port=5432;User ID=xaf;Password=xaf123;Database=xafaireportdesigner;Encoding=UNICODE";
        DefaultConnectionStringProvider.AssignConnectionStrings(
            new Dictionary<string, string>
            {
                ["XafAIReportDesigner"] = xpoConnectionString
            });

        Application.Run(new AIReportDesignerForm(connectionString, schemaService, apiKey,
            configuration["OpenAI:GenerateModel"] ?? "gpt-5.4-mini"));
    }
}
