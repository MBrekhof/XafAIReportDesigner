using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using XafAIReportDesigner.Module.Attributes;

namespace XafAIReportDesigner.Module.BusinessObjects
{
    [DefaultClassOptions]
    [NavigationItem("Sales")]
    [ImageName("BO_OrderItem")]
    [DefaultProperty(nameof(Quantity))]
    [AIVisible]
    [AIDescription("Line items within an order linking products to quantities and pricing")]
    [Table("OrderItems")]
    public class OrderItem : BaseObject
    {
        [Column(TypeName = "decimal(18,2)")]
        public virtual decimal UnitPrice { get; set; }

        public virtual int Quantity { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        [AIDescription("Discount percentage from 0 to 100 (e.g. 5 means 5%); line total = Quantity * UnitPrice * (1 - Discount / 100)")]
        public virtual decimal Discount { get; set; }

        public virtual Guid? OrderId { get; set; }

        [ForeignKey(nameof(OrderId))]
        public virtual Order Order { get; set; }

        public virtual Guid? ProductId { get; set; }

        [ForeignKey(nameof(ProductId))]
        public virtual Product Product { get; set; }
    }
}
