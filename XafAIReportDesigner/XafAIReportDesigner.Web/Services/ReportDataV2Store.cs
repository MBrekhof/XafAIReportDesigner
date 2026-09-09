using Npgsql;

namespace XafAIReportDesigner.Web.Services;

/// <summary>Raw Npgsql access to the XAF ReportDataV2 table (name → layout bytes).</summary>
public sealed class ReportDataV2Store(string connectionString)
{
    public IReadOnlyList<string> ListNames()
    {
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        using var cmd = new NpgsqlCommand(
            "SELECT \"DisplayName\" FROM \"ReportDataV2\" WHERE \"DisplayName\" IS NOT NULL ORDER BY \"DisplayName\"", conn);
        using var reader = cmd.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    /// <summary>Valid for both hosts: the web designer uses the name as a URL segment.</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 256 && name.IndexOfAny(['/', '\\']) < 0;

    public bool Exists(string name)
    {
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        using var cmd = new NpgsqlCommand("SELECT 1 FROM \"ReportDataV2\" WHERE \"DisplayName\" = @name", conn);
        cmd.Parameters.AddWithValue("name", name);
        return cmd.ExecuteScalar() != null;
    }

    public byte[]? Load(string name)
    {
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        using var cmd = new NpgsqlCommand(
            "SELECT \"Content\" FROM \"ReportDataV2\" WHERE \"DisplayName\" = @name", conn);
        cmd.Parameters.AddWithValue("name", name);
        return cmd.ExecuteScalar() as byte[];
    }

    /// <summary>Create-only: the unique index on DisplayName makes a lost race a loud error, never an overwrite.</summary>
    public void Insert(string name, byte[] layout)
    {
        if (!IsValidName(name)) throw new ArgumentException($"Invalid report name '{name}'.", nameof(name));
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        Insert(conn, name, layout);
    }

    /// <summary>Upsert for Modify and the designer's own Save.</summary>
    public void Save(string name, byte[] layout)
    {
        if (!IsValidName(name)) throw new ArgumentException($"Invalid report name '{name}'.", nameof(name));
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        using var update = new NpgsqlCommand(
            "UPDATE \"ReportDataV2\" SET \"Content\" = @content WHERE \"DisplayName\" = @name", conn);
        update.Parameters.AddWithValue("name", name);
        update.Parameters.AddWithValue("content", layout);
        if (update.ExecuteNonQuery() > 0) return;
        Insert(conn, name, layout);
    }

    private static void Insert(NpgsqlConnection conn, string name, byte[] layout)
    {
        using var insert = new NpgsqlCommand(
            "INSERT INTO \"ReportDataV2\" (\"DisplayName\", \"Content\", \"DataTypeName\", \"IsInplaceReport\", \"IsPredefined\") " +
            "VALUES (@name, @content, '', false, false)", conn);
        insert.Parameters.AddWithValue("name", name);
        insert.Parameters.AddWithValue("content", layout);
        insert.ExecuteNonQuery();
    }
}
