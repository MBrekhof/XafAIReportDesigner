using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Text;
using XafAIReportDesigner.Module.Attributes;

namespace XafAIReportDesigner.Module.Services
{
    /// <summary>
    /// Discovers entity metadata via plain reflection — no XAF dependency.
    /// Scans for classes with [AIVisible] attribute and reads [AIDescription],
    /// [Table], [Column], [StringLength], [ForeignKey] attributes.
    /// Suitable for standalone WinForms and Blazor apps.
    /// </summary>
    public sealed class ReflectionSchemaDiscoveryService
    {
        private readonly Assembly _assembly;
        private readonly object _lock = new();
        private SchemaInfo _cached;

        public ReflectionSchemaDiscoveryService(Assembly assembly)
        {
            _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        }

        public SchemaInfo Schema
        {
            get
            {
                if (_cached != null) return _cached;
                lock (_lock)
                {
                    return _cached ??= Discover();
                }
            }
        }

        /// <summary>
        /// Generates a system prompt describing all discovered entities for AI context.
        /// </summary>
        public string GenerateSystemPrompt()
        {
            var schema = Schema;
            var sb = new StringBuilder();

            sb.AppendLine("You are a helpful report design assistant for an order management application.");
            sb.AppendLine("You create reports based on the following data model.");
            sb.AppendLine();
            sb.AppendLine("Available entities and their database tables:");

            foreach (var entity in schema.Entities)
            {
                var tablePart = !string.IsNullOrEmpty(entity.TableName) ? $" (table: {entity.TableName})" : "";
                if (!string.IsNullOrEmpty(entity.Description))
                    sb.AppendLine($"- **{entity.Name}**{tablePart} — {entity.Description}");
                else
                    sb.AppendLine($"- **{entity.Name}**{tablePart}");

                // Include properties with types for report design context
                foreach (var prop in entity.Properties)
                {
                    var colPart = prop.ColumnName != prop.Name ? $" (column: {prop.ColumnName})" : "";
                    var descPart = !string.IsNullOrEmpty(prop.Description) ? $" — {prop.Description}" : "";
                    sb.AppendLine($"  - {prop.Name}: {prop.TypeName}{colPart}{descPart}");
                }

                foreach (var rel in entity.Relationships)
                {
                    var relType = rel.IsCollection ? $"IList<{rel.TargetEntity}>" : rel.TargetEntity;
                    sb.AppendLine($"  - {rel.PropertyName}: {relType} (navigation)");
                }
            }

            sb.AppendLine();
            sb.AppendLine("When designing reports:");
            sb.AppendLine("- Use the entity and property names for data bindings");
            sb.AppendLine("- Use the table and column names for SQL-based data sources");
            sb.AppendLine("- Create clear, well-formatted layouts with appropriate grouping and sorting");

            return sb.ToString();
        }

        private SchemaInfo Discover()
        {
            var entities = new List<EntityInfo>();

            foreach (var type in _assembly.GetTypes())
            {
                var aiVisible = type.GetCustomAttribute<AIVisibleAttribute>();
                if (aiVisible == null || !aiVisible.IsVisible) continue;

                var aiDescription = type.GetCustomAttribute<AIDescriptionAttribute>();
                var tableAttr = type.GetCustomAttribute<TableAttribute>();

                var entityInfo = new EntityInfo
                {
                    Name = type.Name,
                    Description = aiDescription?.Description,
                    TableName = tableAttr?.Name ?? type.Name,
                    ClrType = type,
                };

                // Get all public instance properties
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var prop in properties)
                {
                    // Skip common infrastructure properties
                    if (prop.Name is "ID" or "GCRecord" or "OptimisticLockField")
                        continue;

                    // Skip foreign key ID properties (Guid? ending in Id with ForeignKey on another prop)
                    if (prop.Name.EndsWith("Id", StringComparison.Ordinal) &&
                        (prop.PropertyType == typeof(Guid?) || prop.PropertyType == typeof(Guid)))
                        continue;

                    // Check [AIVisible(false)] to exclude
                    var memberAiVisible = prop.GetCustomAttribute<AIVisibleAttribute>();
                    if (memberAiVisible is { IsVisible: false })
                        continue;

                    var memberAiDescription = prop.GetCustomAttribute<AIDescriptionAttribute>();
                    var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();

                    // Check if it's a collection (navigation)
                    if (IsCollectionProperty(prop))
                    {
                        var elementType = GetCollectionElementType(prop.PropertyType);
                        if (elementType != null)
                        {
                            entityInfo.Relationships.Add(new RelationshipInfo
                            {
                                PropertyName = prop.Name,
                                TargetEntity = elementType.Name,
                                TargetClrType = elementType,
                                IsCollection = true,
                            });
                        }
                        continue;
                    }

                    // Check if it's a reference navigation (class type that has [AIVisible])
                    if (IsNavigationProperty(prop))
                    {
                        entityInfo.Relationships.Add(new RelationshipInfo
                        {
                            PropertyName = prop.Name,
                            TargetEntity = prop.PropertyType.Name,
                            TargetClrType = prop.PropertyType,
                            IsCollection = false,
                        });
                        continue;
                    }

                    // Check if it's a computed/NotMapped property
                    if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                        continue;

                    // Scalar property
                    var propInfo = new EntityPropertyInfo
                    {
                        Name = prop.Name,
                        Description = memberAiDescription?.Description,
                        ColumnName = columnAttr?.Name ?? prop.Name,
                        TypeName = GetFriendlyTypeName(prop.PropertyType),
                        ClrType = prop.PropertyType,
                        IsRequired = !IsNullableType(prop.PropertyType),
                    };

                    // Enum values
                    var underlyingType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    if (underlyingType.IsEnum)
                    {
                        propInfo.EnumValues = Enum.GetNames(underlyingType).ToList();
                    }

                    entityInfo.Properties.Add(propInfo);
                }

                entities.Add(entityInfo);
            }

            return new SchemaInfo { Entities = entities.OrderBy(e => e.Name).ToList() };
        }

        private static bool IsCollectionProperty(PropertyInfo prop)
        {
            if (prop.PropertyType == typeof(string)) return false;
            return prop.PropertyType.IsGenericType &&
                   prop.PropertyType.GetGenericTypeDefinition() == typeof(IList<>);
        }

        private static Type GetCollectionElementType(Type collectionType)
        {
            if (collectionType.IsGenericType && collectionType.GetGenericTypeDefinition() == typeof(IList<>))
                return collectionType.GetGenericArguments()[0];
            return null;
        }

        private static bool IsNavigationProperty(PropertyInfo prop)
        {
            var type = prop.PropertyType;
            // It's a navigation if it's a class (not string, not primitive) and has a ForeignKey relationship
            if (type == typeof(string) || type.IsPrimitive || type.IsValueType) return false;
            // Check if there's a corresponding ForeignKey property or if the property has [ForeignKey]
            return prop.GetCustomAttribute<ForeignKeyAttribute>() != null ||
                   prop.DeclaringType?.GetProperty(prop.Name + "Id") != null;
        }

        private static string GetFriendlyTypeName(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
                return GetFriendlyTypeName(underlying) + "?";

            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "int";
            if (type == typeof(long)) return "long";
            if (type == typeof(decimal)) return "decimal";
            if (type == typeof(double)) return "double";
            if (type == typeof(float)) return "float";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(DateTime)) return "DateTime";
            if (type == typeof(Guid)) return "Guid";
            if (type.IsEnum) return type.Name;
            return type.Name;
        }

        private static bool IsNullableType(Type type) =>
            !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
    }
}
