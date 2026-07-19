using System;
using System.Collections.Generic;
using System.Linq;
using DevExpress.DataAccess.ConnectionParameters;
using DevExpress.DataAccess.Sql;
using DevExpress.XtraReports.UI;

namespace XafAIReportDesigner.Module.Services
{
    /// <summary>
    /// Builds a <see cref="SqlDataSource"/> from the discovered schema: one select query
    /// per entity plus master-detail relations for every foreign key, in both directions —
    /// one-to-many (Invoices.Orders) and lookup (Orders.Customers). The AI generation
    /// path binds against exactly this member tree (see <see cref="DescribeDataMembers"/>).
    /// </summary>
    public static class SchemaSqlDataSourceFactory
    {
        public static SqlDataSource Create(SchemaInfo schema, string connectionName, DataConnectionParametersBase connectionParameters)
        {
            var dataSource = new SqlDataSource(connectionParameters)
            {
                Name = "AppDataSource",
                ConnectionName = connectionName,
            };

            foreach (var entity in schema.Entities)
            {
                var builder = SelectQueryFluentBuilder.AddTable(entity.TableName).SelectColumn("ID");
                foreach (var prop in entity.Properties)
                    builder = builder.SelectColumn(prop.ColumnName);
                foreach (var fk in ForeignKeys(schema).Where(f => f.OwnerTable == entity.TableName))
                    builder = builder.SelectColumn(fk.FkColumn);
                dataSource.Queries.Add(builder.Build(entity.TableName));
            }

            foreach (var fk in ForeignKeys(schema))
            {
                // Self-referencing FKs (Employee.ReportsTo) would produce two relations
                // with the same auto-name — skip them, hierarchies are out of scope here.
                if (fk.OwnerTable == fk.TargetTable) continue;

                // One-to-many: each target row exposes its owner rows (relation "InvoicesOrders").
                dataSource.Relations.Add(new MasterDetailInfo(fk.TargetTable, fk.OwnerTable, "ID", fk.FkColumn)
                    { Name = fk.TargetTable + fk.OwnerTable });
                // Lookup: each owner row exposes its target row (relation "OrdersCustomers").
                dataSource.Relations.Add(new MasterDetailInfo(fk.OwnerTable, fk.TargetTable, fk.FkColumn, "ID")
                    { Name = fk.OwnerTable + fk.TargetTable });
            }

            return dataSource;
        }

        /// <summary>
        /// Describes the member tree of the data source built by <see cref="Create"/> —
        /// appended to the AI schema so generated bindings match by construction.
        /// </summary>
        public static string DescribeDataMembers(SchemaInfo schema)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Report binding rules for the attached data source (follow these exactly):");
            sb.AppendLine("- Set the report's DataMember to the master view name (e.g. \"Invoices\"); the root Detail band repeats once per master row.");
            sb.AppendLine("- A DetailReportBand's DataMember must be an ABSOLUTE relation path that starts at the report's master view and uses RELATION NAMES, e.g. \"Invoices.InvoicesOrders\" and nested \"Invoices.InvoicesOrders.OrdersOrderItems\".");
            sb.AppendLine("- In expressions, reach related rows through relation names: [OrdersCustomers].[CompanyName] returns the order's customer name; Sum([OrdersOrderItems].[Quantity]) aggregates over an order's items.");
            sb.AppendLine();
            sb.AppendLine("Relations available (RelationName: meaning):");
            foreach (var fk in ForeignKeys(schema).Where(f => f.OwnerTable != f.TargetTable))
            {
                sb.AppendLine($"- {fk.TargetTable}{fk.OwnerTable}: from a {fk.TargetTable} row to its {fk.OwnerTable} rows (one-to-many)");
                sb.AppendLine($"- {fk.OwnerTable}{fk.TargetTable}: from an {fk.OwnerTable} row to its single {fk.TargetTable} row (lookup)");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Attaches the factory data source to a generated report. Assigning
        /// XtraReport.DataSource resets band DataMembers that were set without a data
        /// source present, so the members are snapshotted first and reassigned after —
        /// normalized to absolute relation paths ("Invoices.InvoicesOrders.OrdersOrderItems")
        /// because the AI sometimes emits paths relative to the parent band.
        /// </summary>
        public static void Attach(XtraReport report, SchemaInfo schema, string connectionName, DataConnectionParametersBase connectionParameters)
        {
            var rootMember = report.DataMember;
            var saved = new List<(DetailReportBand Band, string Member)>();
            Collect(report.Bands, rootMember, saved);

            report.DataSource = Create(schema, connectionName, connectionParameters);
            report.DataMember = rootMember;
            foreach (var (band, member) in saved)
                band.DataMember = member;

            static void Collect(BandCollection bands, string parentPath, List<(DetailReportBand, string)> saved)
            {
                foreach (var band in bands.OfType<DetailReportBand>())
                {
                    var member = band.DataMember;
                    if (!string.IsNullOrEmpty(parentPath) &&
                        !string.IsNullOrEmpty(member) &&
                        member != parentPath &&
                        !member.StartsWith(parentPath + ".", StringComparison.Ordinal))
                    {
                        member = parentPath + "." + member;
                    }
                    saved.Add((band, member));
                    Collect(band.Bands, member, saved);
                }
            }
        }

        private static IEnumerable<(string OwnerTable, string FkColumn, string TargetTable)> ForeignKeys(SchemaInfo schema)
        {
            foreach (var entity in schema.Entities)
            {
                foreach (var rel in entity.Relationships.Where(r => !r.IsCollection))
                {
                    if (entity.ClrType.GetProperty(rel.PropertyName + "Id") != null)
                    {
                        var target = schema.FindEntity(rel.TargetEntity)?.TableName ?? rel.TargetEntity;
                        yield return (entity.TableName, rel.PropertyName + "Id", target);
                    }
                }
            }
        }
    }
}
