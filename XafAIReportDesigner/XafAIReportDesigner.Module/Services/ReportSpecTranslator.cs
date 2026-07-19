#nullable enable
using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevExpress.DataAccess.ConnectionParameters;
using DevExpress.XtraReports.UI;

namespace XafAIReportDesigner.Module.Services
{
    public record ReportSpec(string Title, string MasterView, bool PagePerMasterRow,
        List<FieldSpec> MasterFields, List<LevelSpec> Levels, List<TotalSpec> Totals);
    public record FieldSpec(string Expression, string? Label, string? Format);
    public record LevelSpec(string Relation, List<FieldSpec> HeaderFields, List<ColumnSpec> Columns);
    public record ColumnSpec(string Expression, string Header, string? Format, bool RightAlign);
    public record TotalSpec(string Label, string Expression, string? Format);

    /// <summary>
    /// Own AI report pipeline, deterministic half: an LLM (any provider) fills the
    /// <see cref="ReportSpec"/> JSON described by <see cref="BuildSystemPrompt"/>; this class
    /// translates the spec into an XtraReport using the binding rules proven against
    /// DevExpress 26.1 (see DOCS/DONE.md and the poc-own-pipeline branch history).
    /// </summary>
    public static class ReportSpecTranslator
    {
        private const string SpecInstructions = """
You produce a REPORT SPECIFICATION as strict JSON (no markdown fences, no commentary).

JSON shape:
{
  "title": string,
  "masterView": string,              // the view the report iterates (one page/section per row)
  "pagePerMasterRow": bool,
  "masterFields": [ {"expression": string, "label": string|null, "format": string|null} ],
  "levels": [                        // nested one-to-many drill-downs, outermost first
    {
      "relation": string,            // EXACTLY ONE relation name, valid from the previous level's entity (or from masterView for the first level)
      "headerFields": [ {"expression": string, "label": string|null, "format": string|null} ],  // shown once per row of THIS level
      "columns": [ {"expression": string, "header": string, "format": string|null, "rightAlign": bool} ]  // table over THIS level's rows; usually only the innermost level has columns
    }
  ],
  "totals": [ {"label": string, "expression": string, "format": string|null} ]   // rendered after the innermost table, once per master row
}

Expression rules (DevExpress criteria language):
- masterFields may use ONLY columns of the master view, e.g. [InvoiceNumber], or functions over them: FormatString('{0:d}', [InvoiceDate]).
- headerFields/columns of a level use that level's entity columns, and lookups via relation names: [OrdersCustomers].[CompanyName].
- Computed columns may use + - * / and parentheses, e.g. [Quantity] * [UnitPrice] * (1 - [Discount] / 100).
- totals use aggregates over the innermost level's rows: Sum(expr), Count(), Avg(expr); literals allowed, e.g. Sum(...) * 0.21.
- Use ONLY columns and relation names that exist in the schema below. Never invent fields.
- Never show raw ID/uuid columns. Do not repeat the same information in masterFields and headerFields.
- format values are FormatString placeholders such as "{0:n2}" or "{0:d}" (or null).
""";

        public static string BuildSystemPrompt(string schemaText) =>
            SpecInstructions + "\n\nDATABASE SCHEMA:\n" + schemaText;

        /// <summary>Parses the LLM response into a spec; returns null if it is not valid JSON.</summary>
        public static ReportSpec? ParseSpec(string rawResponse)
        {
            var text = rawResponse.Trim();
            if (text.StartsWith("```"))
                text = text.Trim('`').Replace("json\n", "").Trim();
            try
            {
                return JsonSerializer.Deserialize<ReportSpec>(text,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static XtraReport BuildReport(ReportSpec spec, SchemaInfo schema,
            string connectionName, DataConnectionParametersBase connectionParams)
        {
            const float PageWidth = 650f;
            var report = new XtraReport
            {
                DataSource = SchemaSqlDataSourceFactory.Create(schema, connectionName, connectionParams),
                DataMember = spec.MasterView,
            };

            var header = new ReportHeaderBand { HeightF = 40 };
            header.Controls.Add(new XRLabel
            {
                Text = spec.Title, WidthF = PageWidth, HeightF = 30,
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
            });
            report.Bands.Add(header);

            // Master fields plus upper-level headerFields (prefixed with their relation path),
            // once per master row. Lookup chains resolve fine from the root band; only the
            // DEEPEST level becomes a DetailReportBand — the proven DX shape. An intermediate
            // band with a prefix of the deep band's path breaks the deep band's iteration.
            var relations = SchemaSqlDataSourceFactory.BuildRelationMap(schema);
            var columns = SchemaSqlDataSourceFactory.BuildColumnMap(schema);

            var rootFields = new List<FieldSpec>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in spec.MasterFields)
            {
                var repaired = field with { Expression = RepairChains(field.Expression, spec.MasterView, relations, columns) };
                if (seen.Add(repaired.Expression)) rootFields.Add(repaired);
            }
            var prefix = new List<string>();
            foreach (var level in spec.Levels.Take(Math.Max(0, spec.Levels.Count - 1)))
            {
                prefix.Add(level.Relation);
                foreach (var field in level.HeaderFields)
                {
                    var prefixed = PrefixChains(field.Expression, prefix);
                    prefixed = RepairChains(prefixed, spec.MasterView, relations, columns);
                    if (seen.Add(prefixed))
                        rootFields.Add(field with { Expression = prefixed });
                }
            }

            var rootDetail = new DetailBand { HeightF = rootFields.Count * 22 + 8 };
            float y = 4;
            foreach (var field in rootFields)
            {
                var label = new XRLabel { WidthF = PageWidth, HeightF = 20, TopF = y, Font = new Font("Segoe UI", 9.75f) };
                label.ExpressionBindings.Add(new ExpressionBinding("Text", WithLabel(field)));
                rootDetail.Controls.Add(label);
                y += 22;
            }
            report.Bands.Add(rootDetail);

            // Single deep band with the full absolute path.
            DetailReportBand? lastBand = null;
            var deepEntity = spec.MasterView;
            foreach (var level in spec.Levels)
                if (relations.TryGetValue((deepEntity, level.Relation), out var target)) deepEntity = target;

            if (spec.Levels.Count > 0)
            {
                var deepest = spec.Levels[^1];
                var path = spec.MasterView + "." + string.Join(".", spec.Levels.Select(l => l.Relation));
                // Explicit DataSource on the band (DX-generated layouts do this) — without it the
                // detail prints only the first row even though footer aggregates see all rows.
                var band = new DetailReportBand
                {
                    Name = "level_" + deepest.Relation,
                    DataSource = report.DataSource,
                    DataMember = path,
                };

                var repairedColumns = deepest.Columns
                    .Select(c => c with { Expression = RepairChains(c.Expression, deepEntity, relations, columns) })
                    .ToList();
                var detail = new DetailBand { HeightF = repairedColumns.Count > 0 ? 24 : 0 };
                if (repairedColumns.Count > 0)
                {
                    var columnsHeader = new GroupHeaderBand { HeightF = 26, Name = "hdr_" + deepest.Relation };
                    columnsHeader.Controls.Add(BuildRow(repairedColumns, PageWidth, isHeader: true));
                    band.Bands.Add(columnsHeader);
                    detail.Controls.Add(BuildRow(repairedColumns, PageWidth, isHeader: false));
                }
                band.Bands.Add(detail);
                report.Bands.Add(band);
                lastBand = band;
            }

            if (spec.Totals.Count > 0 && lastBand != null)
            {
                var footer = new GroupFooterBand { HeightF = spec.Totals.Count * 22 + 8 };
                float ty = 4;
                for (int i = 0; i < spec.Totals.Count; i++)
                {
                    var total = spec.Totals[i];
                    var isLast = i == spec.Totals.Count - 1;
                    footer.Controls.Add(new XRLabel
                    {
                        Text = total.Label, WidthF = 200, HeightF = 20, TopF = ty, LeftF = PageWidth - 360,
                        TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleRight,
                        Font = new Font("Segoe UI", 9.75f, isLast ? FontStyle.Bold : FontStyle.Regular),
                    });
                    // Aggregates must be the TOP-LEVEL expression (a FormatString wrapper defeats
                    // summary evaluation) — format goes in TextFormatString, the proven DX shape.
                    var value = new XRLabel
                    {
                        WidthF = 160, HeightF = 20, TopF = ty, LeftF = PageWidth - 160,
                        TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleRight,
                        Font = new Font("Segoe UI", 9.75f, isLast ? FontStyle.Bold : FontStyle.Regular),
                        TextFormatString = NormalizeFormat(total.Format) ?? "{0:n2}",
                    };
                    var aggregate = Regex.Replace(total.Expression, @"\bsum(Sum|Count|Avg|Min|Max)\b", "$1");
                    aggregate = RepairChains(aggregate, deepEntity, relations, columns);
                    value.ExpressionBindings.Add(new ExpressionBinding("Text", aggregate));
                    footer.Controls.Add(value);
                    ty += 22;
                }
                lastBand.Bands.Add(footer);
            }

            if (spec.PagePerMasterRow && lastBand != null)
                lastBand.PageBreak = PageBreak.AfterBand;

            return report;
        }

        private static XRTable BuildRow(List<ColumnSpec> columns, float pageWidth, bool isHeader)
        {
            var table = new XRTable { WidthF = pageWidth, HeightF = isHeader ? 24 : 22 };
            var row = new XRTableRow();
            table.Rows.Add(row);
            // First column gets double weight (usually the description).
            float unit = pageWidth / (columns.Count + 1);
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var cell = new XRTableCell
                {
                    WidthF = i == 0 ? unit * 2 : unit,
                    Font = new Font("Segoe UI", 9.75f, isHeader ? FontStyle.Bold : FontStyle.Regular),
                    TextAlignment = column.RightAlign
                        ? DevExpress.XtraPrinting.TextAlignment.MiddleRight
                        : DevExpress.XtraPrinting.TextAlignment.MiddleLeft,
                    Padding = new DevExpress.XtraPrinting.PaddingInfo(4, 4, 2, 2),
                };
                if (isHeader)
                {
                    cell.Text = column.Header;
                    cell.BackColor = Color.WhiteSmoke;
                }
                else
                {
                    cell.ExpressionBindings.Add(new ExpressionBinding("Text", Formatted(column.Expression, column.Format)));
                }
                row.Cells.Add(cell);
            }
            return table;
        }

        private static string WithLabel(FieldSpec field)
        {
            var expr = Formatted(field.Expression, field.Format);
            return string.IsNullOrEmpty(field.Label) ? expr : $"'{field.Label}: ' + {expr}";
        }

        // Models emit bare .NET formats ("c2", "n2") as often as placeholders — normalize.
        private static string? NormalizeFormat(string? format) =>
            string.IsNullOrEmpty(format) ? null : format.Contains("{0") ? format : "{0:" + format + "}";

        private static string Formatted(string expression, string? format) =>
            NormalizeFormat(format) is string f ? $"FormatString('{f}', {expression})" : expression;

        private static string PrefixChains(string expression, List<string> prefixSegments)
        {
            var prefixText = string.Concat(prefixSegments.Select(s => $"[{s}]."));
            return Regex.Replace(expression,
                @"\[([A-Za-z_][A-Za-z0-9_]*)\](?:\s*\.\s*\[[A-Za-z0-9_]+\])*",
                m => prefixText + m.Value);
        }

        /// <summary>
        /// Deterministic repair: for every field chain that does not resolve from the given
        /// context entity, drop redundant leading segments or BFS the relation graph for the
        /// shortest prefix path that makes it resolve. The LLM supplies intent; this
        /// guarantees correctness.
        /// </summary>
        private static string RepairChains(string expression, string contextEntity,
            Dictionary<(string, string), string> relations, Dictionary<string, HashSet<string>> columns)
        {
            return Regex.Replace(expression,
                @"\[([A-Za-z_][A-Za-z0-9_]*)\](?:\s*\.\s*\[[A-Za-z0-9_]+\])*",
                m =>
                {
                    var segments = m.Value.Replace("[", "").Replace("]", "").Split('.').Select(s => s.Trim()).ToArray();
                    if (Resolves(contextEntity, segments)) return m.Value;

                    // Over-qualified chain: drop redundant leading segments if the rest resolves.
                    for (int k = 1; k < segments.Length; k++)
                        if (Resolves(contextEntity, segments[k..]))
                            return string.Join(".", segments[k..].Select(s => $"[{s}]"));

                    // Wrong-direction relation segment (e.g. [ProductsOrderItems] used from
                    // OrderItems): swap for the relation from the current entity to the
                    // misused name's master entity, then re-check.
                    if (RepairSegments(contextEntity, segments) is string[] fixedSegments)
                        return string.Join(".", fixedSegments.Select(s => $"[{s}]"));

                    // BFS for the shortest relation path whose end entity resolves the chain.
                    var queue = new Queue<(string Entity, List<string> Path)>();
                    var visited = new HashSet<string> { contextEntity };
                    queue.Enqueue((contextEntity, new List<string>()));
                    while (queue.Count > 0)
                    {
                        var (entity, pathSoFar) = queue.Dequeue();
                        foreach (var ((master, relationName), target) in relations.Select(kv => (kv.Key, kv.Value)))
                        {
                            if (master != entity || !visited.Add(target)) continue;
                            var candidate = new List<string>(pathSoFar) { relationName };
                            if (candidate.Count > 3) continue;
                            if (Resolves(target, segments))
                                return string.Concat(candidate.Select(s => $"[{s}].")) + m.Value;
                            queue.Enqueue((target, candidate));
                        }
                    }
                    return m.Value; // unresolvable — validation will flag it
                });

            bool Resolves(string entity, string[] segments)
            {
                for (int i = 0; i < segments.Length; i++)
                {
                    var isLast = i == segments.Length - 1;
                    if (relations.TryGetValue((entity, segments[i]), out var next)) { entity = next; continue; }
                    if (isLast && columns.TryGetValue(entity, out var cols) && cols.Contains(segments[i])) return true;
                    return false;
                }
                return true;
            }

            string[]? RepairSegments(string entity, string[] segments)
            {
                var result = (string[])segments.Clone();
                for (int i = 0; i < result.Length; i++)
                {
                    var isLast = i == result.Length - 1;
                    if (relations.TryGetValue((entity, result[i]), out var next)) { entity = next; continue; }
                    if (isLast && columns.TryGetValue(entity, out var cols) && cols.Contains(result[i])) return result;

                    // Segment is a relation defined on some OTHER entity: find the relation
                    // from here to that entity and substitute it.
                    var seg = result[i];
                    var misusedMaster = relations.Keys.FirstOrDefault(k => k.Item2 == seg).Item1;
                    if (misusedMaster == null) return null;
                    var replacement = relations.Keys.FirstOrDefault(k => k.Item1 == entity && relations[k] == misusedMaster).Item2;
                    if (replacement == null) return null;
                    result[i] = replacement;
                    entity = misusedMaster;
                }
                return result;
            }
        }
    }
}
