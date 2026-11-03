namespace BatteryEC.Domain.Enums;

public static class InventoryTransactionType
{
    public const string PurchaseReceipt = "PurchaseReceipt";
    public const string OnlineSale = "OnlineSale";
    public const string CounterSale = "CounterSale";
    public const string SupplierReturn = "SupplierReturn";
    public const string AdjustmentIn = "AdjustmentIn";
    public const string AdjustmentOut = "AdjustmentOut";
    public const string AssemblyConsumption = "AssemblyConsumption";
    public const string AssemblyProduction = "AssemblyProduction";
    public const string RepairConsumption = "RepairConsumption";
}
