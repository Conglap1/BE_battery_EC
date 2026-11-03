using BatteryEC.Domain.Entities.Catalog;

namespace BatteryEC.Domain.Entities.Services;

public class RepairTicketItem
{
    public long RepairTicketItemId { get; set; }
    public long RepairTicketId { get; set; }
    public long ProductVariantId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    public RepairTicket RepairTicket { get; set; } = null!;
    public ProductVariant ProductVariant { get; set; } = null!;
}
