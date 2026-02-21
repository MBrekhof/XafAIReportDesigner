using DevExpress.AIIntegration;
using DevExpress.DataAccess;
using Microsoft.Extensions.AI;
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

        // Register OpenAI client for AI report behaviors.
        var apiKey = configuration["OpenAI:ApiKey"];
        var model = configuration["OpenAI:Model"] ?? "gpt-4o";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                "OpenAI API key is not configured.\n\nSet the \"OpenAI:ApiKey\" value in appsettings.Development.json.",
                "Configuration Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        IChatClient chatClient = new OpenAI.OpenAIClient(apiKey)
            .GetChatClient(model)
            .AsIChatClient();

        AIExtensionsContainerDesktop.Default.RegisterChatClient(chatClient);

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

        var schemaPrompt = schemaService.GenerateSystemPrompt();
        Application.Run(new AIReportDesignerForm(connectionString, schemaPrompt));
    }
}
