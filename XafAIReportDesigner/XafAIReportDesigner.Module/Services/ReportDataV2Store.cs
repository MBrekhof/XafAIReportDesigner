#nullable enable
using System;
using System.Collections.Generic;
using Npgsql;

namespace XafAIReportDesigner.Module.Services
{
    /// <summary>
    /// The one persistence path for both designers (RPT-016): raw Npgsql access to the XAF
    /// ReportDataV2 table, addressed by DisplayName (unique index, RPT-011). Names are
    /// validated on every write because the web designer uses them as URL segments.
    /// </summary>
    public sealed class ReportDataV2Store(string connectionString)
    {
        public static bool IsValidName(string? name) =>
            !string.IsNullOrWhiteSpace(name) && name.Length <= 256 && name.IndexOfAny(['/', '\\']) < 0;

        public IReadOnlyList<string> ListNames()
        {
            using var conn = Open();
            using var cmd = new NpgsqlCommand(
                "SELECT \"DisplayName\" FROM \"ReportDataV2\" WHERE \"DisplayName\" IS NOT NULL ORDER BY \"DisplayName\"", conn);
            using var reader = cmd.ExecuteReader();
            var names = new List<string>();
            while (reader.Read()) names.Add(reader.GetString(0));
            return names;
        }

        public bool Exists(string name) => Find(name).Exists;

        /// <summary>Predefined rows belong to XAF code; their Content setter is a no-op, so writes must be refused up front.</summary>
        public (bool Exists, bool IsPredefined) Find(string name)
        {
            using var conn = Open();
            using var cmd = new NpgsqlCommand("SELECT \"IsPredefined\" FROM \"ReportDataV2\" WHERE \"DisplayName\" = @name", conn);
            cmd.Parameters.AddWithValue("name", name);
            return cmd.ExecuteScalar() is bool predefined ? (true, predefined) : (false, false);
        }

        public byte[]? Load(string name)
        {
            using var conn = Open();
            using var cmd = new NpgsqlCommand("SELECT \"Content\" FROM \"ReportDataV2\" WHERE \"DisplayName\" = @name", conn);
            cmd.Parameters.AddWithValue("name", name);
            return cmd.ExecuteScalar() as byte[];
        }

        /// <summary>Create-only: the unique index on DisplayName makes a lost race a loud error, never an overwrite.</summary>
        public void Insert(string name, byte[] layout, string dataTypeName = "")
        {
            Validate(name);
            using var conn = Open();
            Insert(conn, name, layout, dataTypeName);
        }

        /// <summary>Upsert for Modify and the designers' own Save. Refuses predefined rows.</summary>
        public void Save(string name, byte[] layout, string? dataTypeName = null)
        {
            Validate(name);
            using var conn = Open();
            using var update = new NpgsqlCommand(
                "UPDATE \"ReportDataV2\" SET \"Content\" = @content, \"DataTypeName\" = COALESCE(@dataType, \"DataTypeName\") " +
                "WHERE \"DisplayName\" = @name AND NOT \"IsPredefined\"", conn);
            update.Parameters.AddWithValue("name", name);
            update.Parameters.AddWithValue("content", layout);
            update.Parameters.AddWithValue("dataType", (object?)dataTypeName ?? DBNull.Value);
            if (update.ExecuteNonQuery() > 0) return;
            if (Find(name).IsPredefined)
                throw new InvalidOperationException($"'{name}' is a predefined XAF report and cannot be overwritten.");
            Insert(conn, name, layout, dataTypeName ?? "");
        }

        private static void Insert(NpgsqlConnection conn, string name, byte[] layout, string dataTypeName)
        {
            using var insert = new NpgsqlCommand(
                "INSERT INTO \"ReportDataV2\" (\"DisplayName\", \"Content\", \"DataTypeName\", \"IsInplaceReport\", \"IsPredefined\") " +
                "VALUES (@name, @content, @dataType, false, false)", conn);
            insert.Parameters.AddWithValue("name", name);
            insert.Parameters.AddWithValue("content", layout);
            insert.Parameters.AddWithValue("dataType", dataTypeName);
            insert.ExecuteNonQuery();
        }

        private static void Validate(string name)
        {
            if (!IsValidName(name)) throw new ArgumentException($"Invalid report name '{name}'.", nameof(name));
        }

        private NpgsqlConnection Open()
        {
            var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            return conn;
        }
    }
}
