using DevExpress.Blazor.Reporting;
using DevExpress.DataAccess;
using DevExpress.XtraReports.Web.Extensions;
using XafAIReportDesigner.Module.Services;
using XafAIReportDesigner.Web.Components;
using XafAIReportDesigner.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Serve _framework/_content static web assets regardless of environment —
// without this a non-Development run 404s blazor.web.js and all DX resources.
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMvc();
builder.Services.AddDevExpressBlazorReporting();
// Must be registered AFTER AddDevExpressBlazorReporting (DX docs).
builder.Services.AddScoped<ReportStorageWebExtension, ReportDataV2Storage>();

var connectionString = builder.Configuration["Database:ConnectionString"]
    ?? "Host=localhost;Port=5432;Database=xafaireportdesigner;Username=xaf;Password=xaf123";
var xpoConnectionString = builder.Configuration["Database:XpoConnectionString"]
    ?? "XpoProvider=Postgres;Server=localhost;Port=5432;User ID=xaf;Password=xaf123;Database=xafaireportdesigner;Encoding=UNICODE";

// Resolves the name-only connection in saved layouts at preview time.
DefaultConnectionStringProvider.AssignConnectionStrings(
    new Dictionary<string, string> { ["XafAIReportDesigner"] = xpoConnectionString });
builder.Services.AddScoped<DevExpress.DataAccess.Wizard.Services.IConnectionProviderService>(
    _ => new AppConnectionProvider(connectionString));
builder.Services.AddScoped<DevExpress.DataAccess.Web.IConnectionProviderFactory, AppConnectionProviderFactory>();

builder.Services.AddSingleton(new ReflectionSchemaDiscoveryService(
    typeof(XafAIReportDesigner.Module.Attributes.AIVisibleAttribute).Assembly));
builder.Services.AddSingleton(sp => new AIReportService(
    sp.GetRequiredService<ReflectionSchemaDiscoveryService>(),
    connectionString,
    builder.Configuration["OpenAI:ApiKey"] ?? throw new InvalidOperationException(
        "OpenAI:ApiKey is not configured (appsettings.Development.json)."),
    builder.Configuration["OpenAI:GenerateModel"] ?? "gpt-5.4-mini"));
builder.Services.AddSingleton(sp => new ReportDataV2Store(connectionString));

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();
app.UseDevExpressBlazorReporting();
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
