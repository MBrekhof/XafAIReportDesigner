using DevExpress.DataAccess.ConnectionParameters;
using DevExpress.DataAccess.Sql;
using DevExpress.DataAccess.Web;
using DevExpress.DataAccess.Wizard.Services;
using Npgsql;

namespace XafAIReportDesigner.Web.Services;

/// <summary>
/// Resolves the app's name-only connection (layouts never carry credentials) to real
/// PostgreSQL parameters for preview/export — the web equivalent of the WinForms
/// AppConnectionStorageService (DX: IConnectionProviderFactory in DI).
/// </summary>
public sealed class AppConnectionProvider(string connectionString) : IConnectionProviderService
{
    public const string AppConnectionName = "XafAIReportDesigner";

    public SqlDataConnection LoadConnection(string connectionName)
    {
        if (connectionName != AppConnectionName)
            throw new KeyNotFoundException($"Connection '{connectionName}' not found.");
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        return new SqlDataConnection(connectionName,
            new PostgreSqlConnectionParameters(b.Host, b.Port, b.Database, b.Username, b.Password));
    }
}

public sealed class AppConnectionProviderFactory(IConnectionProviderService service) : IConnectionProviderFactory
{
    public IConnectionProviderService Create() => service;
}
