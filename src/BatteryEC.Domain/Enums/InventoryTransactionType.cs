namespace BatteryEC.Domain.Enums;

public static class InventoryTransactionType
{
    public const string PurchaseReceipt = "PurchaseReceipt";
    public const string PurchaseReturn = "PurchaseReturn";
    public const string OnlineSale = "OnlineSale";
    public const string CounterSale = "CounterSale";
    public const string SaleReturn = "SaleReturn";
    public const string RepairConsumption = "RepairConsumption";
    public const string RepairReturn = "RepairReturn";
    public const string AssemblyConsumption = "AssemblyConsumption";
    public const string AssemblyProduction = "AssemblyProduction";
    public const string StockAudit = "StockAudit";
    public const string InitialBalance = "InitialBalance";

    // Aliases for compatibility
    public const string SupplierReturn = "PurchaseReturn";
    public const string AdjustmentIn = "StockAudit";
    public const string AdjustmentOut = "StockAudit";
}
