using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using XafAIReportDesigner.Module.Attributes;

namespace XafAIReportDesigner.Module.BusinessObjects
{
    [DefaultClassOptions]
    [NavigationItem("HR")]
    [ImageName("BO_Department")]
    [AIVisible]
    [AIDescription("Many-to-many association between employees and their assigned territories")]
    [Table("EmployeeTerritories")]
    public class EmployeeTerritory : BaseObject
    {
        public virtual Guid EmployeeId { get; set; }

        public virtual Guid TerritoryId { get; set; }

        [ForeignKey(nameof(EmployeeId))]
        public virtual Employee Employee { get; set; }

        [ForeignKey(nameof(TerritoryId))]
        public virtual Territory Territory { get; set; }
    }
}
