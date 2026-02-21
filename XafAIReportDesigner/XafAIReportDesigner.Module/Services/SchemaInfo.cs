using System;
using System.Collections.Generic;
using System.Linq;

namespace XafAIReportDesigner.Module.Services
{
    public sealed class SchemaInfo
    {
        public List<EntityInfo> Entities { get; set; } = new();

        public EntityInfo FindEntity(string name) =>
            Entities.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public sealed class EntityInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string TableName { get; set; }
        public Type ClrType { get; set; }
        public List<EntityPropertyInfo> Properties { get; set; } = new();
        public List<RelationshipInfo> Relationships { get; set; } = new();
    }

    public sealed class EntityPropertyInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string ColumnName { get; set; }
        public string TypeName { get; set; }
        public Type ClrType { get; set; }
        public bool IsRequired { get; set; }
        public List<string> EnumValues { get; set; } = new();
    }

    public sealed class RelationshipInfo
    {
        public string PropertyName { get; set; }
        public string TargetEntity { get; set; }
        public Type TargetClrType { get; set; }
        public bool IsCollection { get; set; }
    }
}
