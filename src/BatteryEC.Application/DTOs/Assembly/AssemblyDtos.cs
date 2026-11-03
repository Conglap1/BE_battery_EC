namespace BatteryEC.Application.DTOs.Assembly;

public class BomItemDto
{
    public long ComponentVariantId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

public class CreateAssemblyOrderDto
{
    public string AssemblyCode { get; set; } = string.Empty;
    public long BundleVariantId { get; set; }
    public int QuantityProduced { get; set; }
    public string? Note { get; set; }
    public List<CreateAssemblyOrderItemDto> Items { get; set; } = new();
}

public class CreateAssemblyOrderItemDto
{
    public long ComponentVariantId { get; set; }
    public int QuantityConsumed { get; set; }
    public decimal UnitCost { get; set; }
}

public class AssemblyOrderDto
{
    public long AssemblyOrderId { get; set; }
    public string AssemblyCode { get; set; } = string.Empty;
    public long BundleVariantId { get; set; }
    public string BundleVariantName { get; set; } = string.Empty;
    public int QuantityProduced { get; set; }
    public decimal TotalCost { get; set; }
    public decimal UnitCost { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime AssembledAt { get; set; }
    public List<AssemblyOrderItemDto> Items { get; set; } = new();
}

public class AssemblyOrderItemDto
{
    public long AssemblyOrderItemId { get; set; }
    public long ComponentVariantId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public int QuantityConsumed { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
}
