namespace BatteryEC.Domain.Exceptions;

public class InsufficientStockException : DomainException
{
    public long ProductVariantId { get; }
    public int AvailableStock { get; }
    public int RequestedStock { get; }

    public InsufficientStockException(long productVariantId, int availableStock, int requestedStock)
        : base($"Insufficient stock for ProductVariant {productVariantId}. Available: {availableStock}, Requested: {requestedStock}.")
    {
        ProductVariantId = productVariantId;
        AvailableStock = availableStock;
        RequestedStock = requestedStock;
    }
}
