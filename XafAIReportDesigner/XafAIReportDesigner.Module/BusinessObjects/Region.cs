using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using XafAIReportDesigner.Module.Attributes;

namespace XafAIReportDesigner.Module.BusinessObjects
{
    [DefaultClassOptions]
    [NavigationItem("Geography")]
    [ImageName("BO_Country")]
    [DefaultProperty(nameof(Name))]
    [AIVisible]
    [AIDescription("Geographic regions containing territories")]
    [Table("Regions")]
    public class Region : BaseObject
    {
        [StringLength(100)]
        public virtual string Name { get; set; }

        public virtual IList<Territory> Territories { get; set; } = new ObservableCollection<Territory>();
    }
}
