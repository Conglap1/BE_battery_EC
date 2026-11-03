namespace BatteryEC.Application.DTOs.Repairs;

public class CreateRepairTicketDto
{
    public long? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string? SerialOrPinCode { get; set; }
    public string IssueDescription { get; set; } = string.Empty;
    public decimal EstimatedCost { get; set; }
    public int WarrantyMonths { get; set; }
}

public class RepairTicketDto
{
    public long RepairTicketId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public long? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string? SerialOrPinCode { get; set; }
    public string IssueDescription { get; set; } = string.Empty;
    public string? SolutionNote { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal FinalCost { get; set; }
    public decimal PaidAmount { get; set; }
    public string TicketStatus { get; set; } = string.Empty;
    public int WarrantyMonths { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public long CreatedByUserId { get; set; }
    public long? TechnicianId { get; set; }
    public List<RepairTicketItemDto> Items { get; set; } = new();
}

public class RepairTicketItemDto
{
    public long RepairTicketItemId { get; set; }
    public long ProductVariantId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
