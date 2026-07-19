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
            sb.AppendLine("- Each DetailReportBand may add exactly ONE relation segment beyond its parent's path — to traverse two relations, NEST two DetailReportBands (a single band with a two-hop path only reads the first related row).");
            sb.AppendLine("- In expressions, reach related rows through relation names: [OrdersCustomers].[CompanyName] returns the order's customer name; Sum([OrdersOrderItems].[Quantity]) aggregates over an order's items.");
            sb.AppendLine("- Scalar fields of the current row bind directly: a label in a band bound to \"Invoices\" uses plain [InvoiceNumber] or Concat('Date: ', FormatString('{0:d}', [InvoiceDate])). Never leave a label's expression empty ([]).");
            sb.AppendLine("- Expressions are relative to the band's own row context: in a band whose DataMember ends at Orders, aggregate with Sum([OrdersOrderItems].[Quantity]) — NOT with the full path from the master view ([InvoicesOrders].[...] does not exist on an Orders row).");
            sb.AppendLine("- Use ONLY the columns listed above — do not invent fields (no [Description], no [VatRate]). Constants such as a VAT rate are numeric literals in the expression.");
            sb.AppendLine("- Keep each master row's header, detail table, and totals together so they print on the same page; insert the page break after the master row's totals (e.g. GroupFooter/DetailReportBand PageBreak = AfterBand).");
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

        /// <summary>
        /// Validates every expression binding and DetailReportBand path in a generated
        /// report against the schema + relation graph. Returns human-readable issues —
        /// exactly the failures generation varies on (unresolvable field paths, empty []
        /// operands, wrong relation names). Feed non-empty results back through a repair
        /// request (the API accepts an existing report to update).
        /// </summary>
        public static IReadOnlyList<string> ValidateBindings(XtraReport report, SchemaInfo schema)
        {
            var issues = new List<string>();
            var relations = BuildRelationMap(schema);
            var columns = BuildColumnMap(schema);

            ValidateBands(report.Bands, ResolveContext(report.DataMember, relations, columns, issues, "report"));
            return issues;

            void ValidateBands(BandCollection bands, string contextEntity)
            {
                foreach (Band band in bands)
                {
                    var bandContext = contextEntity;
                    if (band is DetailReportBand detailReport)
                        bandContext = ResolveContext(detailReport.DataMember, relations, columns, issues, $"band '{band.Name}'");

                    foreach (var binding in band.ExpressionBindings.Cast<ExpressionBinding>())
                        ValidateExpression(binding.Expression, band.Name, band.Name, bandContext);
                    ValidateControls(band, band, bandContext);
                    if (band is DetailReportBand nested)
                        ValidateBands(nested.Bands, bandContext);
                }
            }

            void ValidateControls(XRControl control, Band band, string contextEntity)
            {
                foreach (XRControl child in control.Controls)
                {
                    // Nested bands surface in Controls too — the band walk validates them
                    // with their own data context.
                    if (child is Band) continue;
                    foreach (var binding in child.ExpressionBindings.Cast<ExpressionBinding>())
                        ValidateExpression(binding.Expression, child.Name, band.Name, contextEntity);
                    ValidateControls(child, band, contextEntity);
                }
            }

            void ValidateExpression(string expression, string controlName, string bandName, string contextEntity)
            {
                if (string.IsNullOrWhiteSpace(expression) || contextEntity == null) return;
                if (expression.Contains("[]"))
                {
                    issues.Add($"Control '{controlName}' (band '{bandName}'): expression contains an empty operand [] — bind it to a real field.");
                    return;
                }

                foreach (var chain in ExtractFieldChains(expression))
                {
                    var entity = contextEntity;
                    for (int i = 0; i < chain.Count; i++)
                    {
                        var segment = chain[i];
                        var isLast = i == chain.Count - 1;
                        if (relations.TryGetValue((entity, segment), out var next))
                        {
                            entity = next;
                            continue;
                        }
                        if (isLast && columns[entity].Contains(segment))
                            break;
                        issues.Add($"Control '{controlName}' (band '{bandName}'): [{string.Join("].[", chain)}] does not resolve — '{segment}' is not a column or relation of \"{entity}\".");
                        break;
                    }
                }
            }
        }

        private static string ResolveContext(string dataMember, Dictionary<(string, string), string> relations,
            Dictionary<string, HashSet<string>> columns, List<string> issues, string owner)
        {
            if (string.IsNullOrEmpty(dataMember)) return null;
            var segments = dataMember.Split('.');
            if (!columns.ContainsKey(segments[0]))
            {
                issues.Add($"{owner}: DataMember '{dataMember}' does not start with a known view.");
                return null;
            }
            var entity = segments[0];
            foreach (var segment in segments.Skip(1))
            {
                if (!relations.TryGetValue((entity, segment), out entity))
                {
                    issues.Add($"{owner}: DataMember '{dataMember}' — '{segment}' is not a relation of the preceding view.");
                    return null;
                }
            }
            return entity;
        }

        public static Dictionary<(string Master, string RelationName), string> BuildRelationMap(SchemaInfo schema)
        {
            var map = new Dictionary<(string, string), string>();
            foreach (var fk in ForeignKeys(schema).Where(f => f.OwnerTable != f.TargetTable))
            {
                map[(fk.TargetTable, fk.TargetTable + fk.OwnerTable)] = fk.OwnerTable;
                map[(fk.OwnerTable, fk.OwnerTable + fk.TargetTable)] = fk.TargetTable;
            }
            return map;
        }

        public static Dictionary<string, HashSet<string>> BuildColumnMap(SchemaInfo schema)
        {
            var map = new Dictionary<string, HashSet<string>>();
            foreach (var entity in schema.Entities)
            {
                var cols = new HashSet<string>(StringComparer.Ordinal) { "ID" };
                foreach (var prop in entity.Properties) cols.Add(prop.ColumnName);
                foreach (var fk in ForeignKeys(schema).Where(f => f.OwnerTable == entity.TableName)) cols.Add(fk.FkColumn);
                map[entity.TableName] = cols;
            }
            return map;
        }

        /// <summary>
        /// Extracts bracketed field chains ("[OrdersCustomers].[CompanyName]" → two segments)
        /// from a criteria expression. Report parameters (?name) are not bracketed and are ignored.
        /// </summary>
        private static IEnumerable<List<string>> ExtractFieldChains(string expression)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(expression,
                @"\[([A-Za-z_][A-Za-z0-9_]*)\](?:\s*\.\s*\[([A-Za-z_][A-Za-z0-9_]*)\])*");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var chain = new List<string> { m.Groups[1].Value };
                foreach (System.Text.RegularExpressions.Capture c in m.Groups[2].Captures)
                    chain.Add(c.Value);
                yield return chain;
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
