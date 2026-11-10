using System.Linq.Expressions;
using System.Reflection;
using BatteryEC.Application.DTOs.Assembly;
using BatteryEC.Application.DTOs.Pos;
using BatteryEC.Application.Services;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Entities.Operations;
using BatteryEC.Domain.Entities.Pos;
using BatteryEC.Domain.Entities.Production;
using BatteryEC.Domain.Entities.Purchasing;
using BatteryEC.Domain.Entities.Sales;
using BatteryEC.Domain.Entities.Services;
using BatteryEC.Domain.Enums;
using BatteryEC.Domain.Repositories;
using BatteryEC.Infrastructure.Persistence;
using BatteryEC.WebApi.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BatteryEC.UnitTests.Adversarial;

/// <summary>
/// Empirical Challenger 1 Test Suite for R1 through R4.
/// Stress-tests computed columns, BOM assembly, two-tier inventory, and POS retail workflows.
/// </summary>
public class EmpiricalChallengerR1R4Tests
{
    private const string PgConnectionString = "Host=localhost;Port=5432;Database=battery_ec;Username=postgres;Password=postgres";

    // =========================================================================
    // SECTION 1: R1 — EF Core Computed Columns (ISSUE-03 & ISSUE-11)
    // =========================================================================

    [Fact]
    public void R1_EFCoreMetadata_AllSixComputedColumns_MarkedAsValueGeneratedOnAddOrUpdate()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(PgConnectionString)
                      .UseSnakeCaseNamingConvention();

        using var context = new BatteryDbContext(optionsBuilder.Options);
        var model = context.Model;

        // 1. assembly_orders.unit_cost
        var aoEntity = model.FindEntityType(typeof(AssemblyOrder));
        Assert.NotNull(aoEntity);
        var aoUnitCost = aoEntity.FindProperty(nameof(AssemblyOrder.UnitCost));
        Assert.NotNull(aoUnitCost);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, aoUnitCost.ValueGenerated);

        // 2. assembly_order_items.line_total
        var aoiEntity = model.FindEntityType(typeof(AssemblyOrderItem));
        Assert.NotNull(aoiEntity);
        var aoiLineTotal = aoiEntity.FindProperty(nameof(AssemblyOrderItem.LineTotal));
        Assert.NotNull(aoiLineTotal);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, aoiLineTotal.ValueGenerated);

        // 3. order_items.line_total
        var oiEntity = model.FindEntityType(typeof(OrderItem));
        Assert.NotNull(oiEntity);
        var oiLineTotal = oiEntity.FindProperty(nameof(OrderItem.LineTotal));
        Assert.NotNull(oiLineTotal);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, oiLineTotal.ValueGenerated);

        // 4. purchase_receipt_items.line_total
        var priEntity = model.FindEntityType(typeof(PurchaseReceiptItem));
        Assert.NotNull(priEntity);
        var priLineTotal = priEntity.FindProperty(nameof(PurchaseReceiptItem.LineTotal));
        Assert.NotNull(priLineTotal);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, priLineTotal.ValueGenerated);

        // 5. sale_items.line_total
        var siEntity = model.FindEntityType(typeof(SaleItem));
        Assert.NotNull(siEntity);
        var siLineTotal = siEntity.FindProperty(nameof(SaleItem.LineTotal));
        Assert.NotNull(siLineTotal);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, siLineTotal.ValueGenerated);

        // 6. repair_ticket_items.line_total
        var rtiEntity = model.FindEntityType(typeof(RepairTicketItem));
        Assert.NotNull(rtiEntity);
        var rtiLineTotal = rtiEntity.FindProperty(nameof(RepairTicketItem.LineTotal));
        Assert.NotNull(rtiLineTotal);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rtiLineTotal.ValueGenerated);
    }

    [Fact]
    public async Task R1_EmpiricalLivePostgreSql_InsertComputedColumns_SucceedsWithout42601()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(PgConnectionString)
                      .UseSnakeCaseNamingConvention();

        await using var context = new BatteryDbContext(optionsBuilder.Options);

        // Run inside transaction and rollback to keep database clean
        await using var tx = await context.Database.BeginTransactionAsync();
        try
        {
            // 1. Verify assembly_orders (unit_cost) and assembly_order_items (line_total)
            var testAo = new AssemblyOrder
            {
                AssemblyCode = $"TEST-ASM-{DateTime.UtcNow.Ticks}",
                BundleVariantId = 1,
                QuantityProduced = 5,
                TotalCost = 500000m,
                Status = "Completed",
                CreatedByUserId = 3,
                AssembledAt = DateTime.UtcNow
            };
            context.AssemblyOrders.Add(testAo);
            await context.SaveChangesAsync();

            Assert.True(testAo.AssemblyOrderId > 0);
            Assert.Equal(100000.00m, testAo.UnitCost); // DB computed 500000 / 5 = 100000

            var testAoi = new AssemblyOrderItem
            {
                AssemblyOrderId = testAo.AssemblyOrderId,
                ComponentVariantId = 1,
                QuantityConsumed = 4,
                UnitCost = 25000m
            };
            context.AssemblyOrderItems.Add(testAoi);
            await context.SaveChangesAsync();

            Assert.True(testAoi.AssemblyOrderItemId > 0);
            Assert.Equal(100000.00m, testAoi.LineTotal); // DB computed 4 * 25000 = 100000

            // 2. Verify order_items (line_total)
            var testOi = new OrderItem
            {
                OrderId = 1,
                ProductVariantId = 1,
                ProductName = "Test Cell",
                VariantName = "VTC6",
                SKU = "TEST-SKU-ORD",
                UnitPrice = 50000m,
                CostPrice = 30000m,
                Quantity = 4,
                WarrantyMonths = 12
            };
            context.OrderItems.Add(testOi);
            await context.SaveChangesAsync();

            Assert.True(testOi.OrderItemId > 0);
            Assert.Equal(200000.00m, testOi.LineTotal); // DB computed 4 * 50000 = 200000

            // 3. Verify purchase_receipt_items (line_total)
            var testPri = new PurchaseReceiptItem
            {
                PurchaseReceiptId = 1,
                ProductVariantId = 1,
                Quantity = 10,
                UnitCost = 40000m
            };
            context.PurchaseReceiptItems.Add(testPri);
            await context.SaveChangesAsync();

            Assert.True(testPri.PurchaseReceiptItemId > 0);
            Assert.Equal(400000.00m, testPri.LineTotal); // DB computed 10 * 40000 = 400000

            // 4. Verify sale_items (line_total)
            var testSi = new SaleItem
            {
                SaleId = 1,
                ProductVariantId = 1,
                ProductName = "Test Cell",
                VariantName = "VTC6",
                SKU = "TEST-SKU-POS",
                UnitPrice = 50000m,
                CostPrice = 30000m,
                Quantity = 3,
                WarrantyMonths = 6
            };
            context.SaleItems.Add(testSi);
            await context.SaveChangesAsync();

            Assert.True(testSi.SaleItemId > 0);
            Assert.Equal(150000.00m, testSi.LineTotal); // DB computed 3 * 50000 = 150000

            // 5. Verify repair_ticket_items (line_total)
            var testRti = new RepairTicketItem
            {
                RepairTicketId = 1,
                ProductVariantId = 1,
                Quantity = 2,
                UnitPrice = 80000m
            };
            context.RepairTicketItems.Add(testRti);
            await context.SaveChangesAsync();

            Assert.True(testRti.RepairTicketItemId > 0);
            Assert.Equal(160000.00m, testRti.LineTotal); // DB computed 2 * 80000 = 160000
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    // =========================================================================
    // SECTION 2: R2 — BOM Assembly & Pack Cost Price (ISSUE-01 & ISSUE-02)
    // =========================================================================

    [Fact]
    public async Task R2_AssembleAsync_Success_PersistsAssemblyOrderId_UpdatesBundleCostPrice_AndRecordsTransactions()
    {
        var uow = new TestUnitOfWork();
        var aoRepo = new TestAssemblyOrderRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();

        var bundleVariant = new ProductVariant
        {
            ProductVariantId = 100,
            VariantName = "Pin 18V 5Ah",
            CostPrice = 0m,
            SellingPrice = 850000m
        };
        varRepo.Add(bundleVariant);

        var comp1 = new ProductVariant { ProductVariantId = 201, VariantName = "Cell 18650", CostPrice = 40000m, SellingPrice = 60000m };
        var comp2 = new ProductVariant { ProductVariantId = 202, VariantName = "BMS 5S 30A", CostPrice = 100000m, SellingPrice = 150000m };
        varRepo.Add(comp1);
        varRepo.Add(comp2);

        invRepo.Add(new Inventory { ProductVariantId = 201, QuantityOnHand = 50, QuantityReserved = 0, Version = 1 });
        invRepo.Add(new Inventory { ProductVariantId = 202, QuantityOnHand = 10, QuantityReserved = 0, Version = 1 });

        var service = new AssemblyService(aoRepo, invRepo, varRepo, uow);

        var dto = new CreateAssemblyOrderDto
        {
            BundleVariantId = 100,
            QuantityProduced = 2,
            Note = "Test Assembly Batch 1",
            Items = new List<CreateAssemblyOrderItemDto>
            {
                new() { ComponentVariantId = 201, QuantityConsumed = 10, UnitCost = 40000m }, // 400,000
                new() { ComponentVariantId = 202, QuantityConsumed = 2, UnitCost = 100000m }   // 200,000
            }
        };

        var result = await service.ExecuteAssemblyOrderAsync(dto, userId: 9);

        // 1. Order details
        Assert.NotNull(result);
        Assert.True(result.AssemblyOrderId > 0);
        Assert.Equal(600000m, result.TotalCost);
        Assert.Equal(300000m, result.UnitCost); // 600,000 / 2 = 300,000

        // 2. ISSUE-01: Pack Pin variant cost price updated
        Assert.Equal(300000m, bundleVariant.CostPrice);
        Assert.Contains(bundleVariant, varRepo.UpdatedVariants);

        // 3. Component inventory decremented
        var comp1Inv = await invRepo.GetByVariantIdAsync(201);
        Assert.NotNull(comp1Inv);
        Assert.Equal(40, comp1Inv.QuantityOnHand); // 50 - 10 = 40

        var comp2Inv = await invRepo.GetByVariantIdAsync(202);
        Assert.NotNull(comp2Inv);
        Assert.Equal(8, comp2Inv.QuantityOnHand); // 10 - 2 = 8

        // 4. Bundle inventory incremented
        var bundleInv = await invRepo.GetByVariantIdAsync(100);
        Assert.NotNull(bundleInv);
        Assert.Equal(2, bundleInv.QuantityOnHand);

        // 5. ISSUE-02: Zero ReferenceId eliminated in all ledger transactions
        Assert.Equal(3, invRepo.Transactions.Count); // 2 consumption + 1 production
        foreach (var tx in invRepo.Transactions)
        {
            Assert.Equal("AssemblyOrder", tx.ReferenceType);
            Assert.Equal(result.AssemblyOrderId, tx.ReferenceId); // Must match order ID!
            Assert.True(tx.ReferenceId > 0, "ReferenceId must not be 0");
        }

        var prodTx = invRepo.Transactions.First(t => t.TransactionType == InventoryTransactionType.AssemblyProduction);
        Assert.Equal(100, prodTx.ProductVariantId);
        Assert.Equal(2, prodTx.QuantityChange);
        Assert.Equal(300000m, prodTx.UnitCost);

        Assert.True(uow.IsCommitted);
    }

    [Fact]
    public async Task R2_AssembleAsync_AvailableStockInsufficient_ThrowsAndDoesNotDeduct()
    {
        var uow = new TestUnitOfWork();
        var aoRepo = new TestAssemblyOrderRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();

        var bundleVariant = new ProductVariant { ProductVariantId = 100, VariantName = "Pack 12V" };
        var compVariant = new ProductVariant { ProductVariantId = 201, VariantName = "Cell 18650" };
        varRepo.Add(bundleVariant);
        varRepo.Add(compVariant);

        //OnHand = 10, Reserved = 8 => Available = 2
        invRepo.Add(new Inventory { ProductVariantId = 201, QuantityOnHand = 10, QuantityReserved = 8, Version = 1 });

        var service = new AssemblyService(aoRepo, invRepo, varRepo, uow);

        var dto = new CreateAssemblyOrderDto
        {
            BundleVariantId = 100,
            QuantityProduced = 1,
            Items = new List<CreateAssemblyOrderItemDto>
            {
                new() { ComponentVariantId = 201, QuantityConsumed = 5, UnitCost = 40000m } // Requires 5, only 2 available
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAssemblyOrderAsync(dto, userId: 1));
        Assert.Contains("không đủ tồn kho khả dụng", ex.Message);

        // Verify stock was not deducted
        var inv = await invRepo.GetByVariantIdAsync(201);
        Assert.NotNull(inv);
        Assert.Equal(10, inv.QuantityOnHand);
        Assert.Equal(8, inv.QuantityReserved);
        Assert.Empty(invRepo.Transactions);
        Assert.True(uow.IsRolledBack);
    }

    [Fact]
    public async Task R2_AssembleAsync_DuplicateComponentLines_SumsConsumedAndValidatesAgainstAvailableStock()
    {
        var uow = new TestUnitOfWork();
        var aoRepo = new TestAssemblyOrderRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();

        var bundleVariant = new ProductVariant { ProductVariantId = 100, VariantName = "Pack 12V" };
        var compVariant = new ProductVariant { ProductVariantId = 201, VariantName = "Cell 18650" };
        varRepo.Add(bundleVariant);
        varRepo.Add(compVariant);

        //OnHand = 10, Reserved = 5 => Available = 5
        invRepo.Add(new Inventory { ProductVariantId = 201, QuantityOnHand = 10, QuantityReserved = 5, Version = 1 });

        var service = new AssemblyService(aoRepo, invRepo, varRepo, uow);

        // 2 lines consuming same component: 3 + 4 = 7 (> Available 5)
        var dto = new CreateAssemblyOrderDto
        {
            BundleVariantId = 100,
            QuantityProduced = 1,
            Items = new List<CreateAssemblyOrderItemDto>
            {
                new() { ComponentVariantId = 201, QuantityConsumed = 3, UnitCost = 40000m },
                new() { ComponentVariantId = 201, QuantityConsumed = 4, UnitCost = 40000m }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAssemblyOrderAsync(dto, userId: 1));
        Assert.Contains("không đủ tồn kho khả dụng", ex.Message);
        Assert.True(uow.IsRolledBack);
    }

    [Theory]
    [InlineData(0, 1, 1)]      // BundleVariantId <= 0
    [InlineData(-5, 1, 1)]     // Negative BundleVariantId
    [InlineData(100, 0, 1)]    // QuantityProduced <= 0
    [InlineData(100, -2, 1)]   // Negative QuantityProduced
    [InlineData(100, 1, 0)]    // Component QuantityConsumed <= 0
    [InlineData(100, 1, -1)]   // Negative Component QuantityConsumed
    public async Task R2_AssembleAsync_BoundaryValidation_RejectsInvalidInputs(long bundleId, int qtyProduced, int consumedQty)
    {
        var uow = new TestUnitOfWork();
        var aoRepo = new TestAssemblyOrderRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();
        var service = new AssemblyService(aoRepo, invRepo, varRepo, uow);

        var dto = new CreateAssemblyOrderDto
        {
            BundleVariantId = bundleId,
            QuantityProduced = qtyProduced,
            Items = new List<CreateAssemblyOrderItemDto>
            {
                new() { ComponentVariantId = 201, QuantityConsumed = consumedQty, UnitCost = 10000m }
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAssemblyOrderAsync(dto, userId: 1));
    }

    // =========================================================================
    // SECTION 3: R3 — Two-Tier Inventory & 3PL Online Order Lifecycle (ISSUE-04, ISSUE-07, ISSUE-17, ISSUE-18)
    // =========================================================================

    [Fact]
    public async Task R3_ShipOrderAsync_HandoverTo3PL_DeductsPhysicalStock_ReleasesReservation_AndLogsOnlineSale()
    {
        var uow = new TestUnitOfWork();
        var orderRepo = new TestOrderRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 50,
            QuantityReserved = 5,
            Version = 1
        };
        invRepo.Add(inventory);

        var order = new Order
        {
            OrderId = 301,
            OrderCode = "ORD-301",
            OrderStatus = "Confirmed",
            ShippingMethodId = 1,
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 10, Quantity = 5, UnitPrice = 100000m, CostPrice = 60000m }
            }
        };
        orderRepo.Add(order);

        var service = new OrderService(orderRepo, invRepo, varRepo, uow);

        var success = await service.ShipOrderAsync(301, "GHTK", "TRACK-999", shippedByUserId: 42);

        Assert.True(success);
        Assert.Equal("Shipped", order.OrderStatus);
        Assert.Single(order.Shipments);
        var shipment = order.Shipments.First();
        Assert.Equal("InTransit", shipment.ShipmentStatus);
        Assert.Equal("GHTK", shipment.CarrierName);
        Assert.Equal("TRACK-999", shipment.TrackingNumber);
        Assert.NotNull(shipment.ShippedAt);

        // Two-Tier Inventory Check:
        // Physical stock decremented upon 3PL handover!
        Assert.Equal(45, inventory.QuantityOnHand);
        // Reservation released!
        Assert.Equal(0, inventory.QuantityReserved);
        Assert.Equal(2, inventory.Version);

        // Ledger transaction recorded
        Assert.Single(invRepo.Transactions);
        var tx = invRepo.Transactions.First();
        Assert.Equal("OnlineSale", tx.TransactionType);
        Assert.Equal("Order", tx.ReferenceType);
        Assert.Equal(301, tx.ReferenceId);
        Assert.Equal(-5, tx.QuantityChange);
        Assert.Equal(60000m, tx.UnitCost);
        Assert.Equal(42, tx.CreatedByUserId);

        Assert.True(uow.IsCommitted);
    }

    [Fact]
    public async Task R3_CompleteOrderAsync_CustomerDelivered_ActivatesWarranty_WithoutDoubleDeductingStock()
    {
        var uow = new TestUnitOfWork();
        var orderRepo = new TestOrderRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 45,
            QuantityReserved = 0,
            Version = 2
        };
        invRepo.Add(inventory);

        var order = new Order
        {
            OrderId = 301,
            OrderCode = "ORD-301",
            OrderStatus = "Shipped",
            PaymentStatus = "Pending",
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 10, Quantity = 5, UnitPrice = 100000m, CostPrice = 60000m }
            },
            Shipments = new List<Shipment>
            {
                new()
                {
                    ShipmentId = 1,
                    OrderId = 301,
                    CarrierName = "GHTK",
                    TrackingNumber = "TRACK-999",
                    ShipmentStatus = "InTransit",
                    ShippedAt = DateTime.UtcNow.AddDays(-2)
                }
            }
        };
        orderRepo.Add(order);

        var service = new OrderService(orderRepo, invRepo, varRepo, uow);

        var success = await service.CompleteOrderAsync(301);

        Assert.True(success);
        Assert.Equal("Completed", order.OrderStatus);
        Assert.Equal("Paid", order.PaymentStatus);
        Assert.NotNull(order.CompletedAt);

        // ISSUE-07: Shipment updated to Delivered and DeliveredAt populated (activates warranty!)
        var shipment = order.Shipments.First();
        Assert.Equal("Delivered", shipment.ShipmentStatus);
        Assert.NotNull(shipment.DeliveredAt);

        // NO redundant stock deduction! Stock must remain exactly as was.
        Assert.Equal(45, inventory.QuantityOnHand);
        Assert.Equal(0, inventory.QuantityReserved);
        Assert.Equal(2, inventory.Version);
        Assert.Empty(invRepo.Transactions); // No extra transactions logged in CompleteOrder

        Assert.True(uow.IsCommitted);
    }

    [Fact]
    public async Task R3_CancelOrderAsync_ShippedOrder_EnforcesNoReturnPolicy()
    {
        var uow = new TestUnitOfWork();
        var orderRepo = new TestOrderRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 45,
            QuantityReserved = 0
        };
        invRepo.Add(inventory);

        var order = new Order
        {
            OrderId = 302,
            OrderStatus = "Shipped", // Already handed to 3PL carrier
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 10, Quantity = 5 }
            }
        };
        orderRepo.Add(order);

        var service = new OrderService(orderRepo, invRepo, varRepo, uow);

        // Attempting to cancel shipped lithium order must be blocked by No-Return Policy
        var result = await service.CancelOrderAsync(302);

        Assert.False(result);
        Assert.Equal("Shipped", order.OrderStatus);
        Assert.Equal(45, inventory.QuantityOnHand);
        Assert.Equal(0, inventory.QuantityReserved);
        Assert.True(uow.IsRolledBack);
    }

    [Fact]
    public async Task R3_CancelOrderAsync_ConfirmedOrder_ReleasesReservation()
    {
        var uow = new TestUnitOfWork();
        var orderRepo = new TestOrderRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 50,
            QuantityReserved = 5,
            Version = 1
        };
        invRepo.Add(inventory);

        var order = new Order
        {
            OrderId = 303,
            OrderStatus = "Confirmed",
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 10, Quantity = 5 }
            }
        };
        orderRepo.Add(order);

        var service = new OrderService(orderRepo, invRepo, varRepo, uow);

        var result = await service.CancelOrderAsync(303);

        Assert.True(result);
        Assert.Equal("Cancelled", order.OrderStatus);
        Assert.Equal(50, inventory.QuantityOnHand); // Physical stock untouched
        Assert.Equal(0, inventory.QuantityReserved); // Reservation released!
        Assert.Equal(2, inventory.Version);
        Assert.True(uow.IsCommitted);
    }

    [Fact]
    public void R3_OrdersController_ShipOrder_HasWarehouseStaffRoleInAuthorize()
    {
        var method = typeof(OrdersController).GetMethod(nameof(OrdersController.ShipOrder));
        Assert.NotNull(method);

        var authAttr = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.NotNull(authAttr.Roles);
        Assert.Contains(RoleType.WarehouseStaff, authAttr.Roles);
        Assert.Contains(RoleType.StoreManager, authAttr.Roles);
        Assert.Contains(RoleType.Admin, authAttr.Roles);
    }

    // =========================================================================
    // SECTION 4: R4 — POS Counter Sales & Inventory (ISSUE-05, ISSUE-06, ISSUE-32)
    // =========================================================================

    [Fact]
    public async Task R4_PosSale_AvailableStockInsufficient_ThrowsAndPreventsDbConstraintViolation()
    {
        var uow = new TestUnitOfWork();
        var saleRepo = new TestSaleRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();

        var variant = new ProductVariant
        {
            ProductVariantId = 50,
            VariantName = "Pin 21700",
            SellingPrice = 90000m,
            CostPrice = 60000m
        };
        varRepo.Add(variant);

        //OnHand = 10, Reserved = 8 => Available = 2
        invRepo.Add(new Inventory
        {
            ProductVariantId = 50,
            QuantityOnHand = 10,
            QuantityReserved = 8,
            Version = 1
        });

        var service = new PosService(saleRepo, invRepo, varRepo, uow);

        // Customer attempts to buy 3 at counter (exceeds available stock 2, would violate check reserved <= on_hand)
        var dto = new CreateSaleDto
        {
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemDto>
            {
                new() { ProductVariantId = 50, Quantity = 3 }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateSaleAsync(dto, cashierUserId: 5));
        Assert.Contains("không đủ tồn kho khả dụng", ex.Message);

        // Verify inventory untouched and transaction rolled back
        var inv = await invRepo.GetByVariantIdAsync(50);
        Assert.NotNull(inv);
        Assert.Equal(10, inv.QuantityOnHand);
        Assert.Equal(8, inv.QuantityReserved);
        Assert.Empty(invRepo.Transactions);
        Assert.True(uow.IsRolledBack);
    }

    [Fact]
    public async Task R4_PosSale_Success_DeductsPhysicalStock_UsesRealSaleIdInLedger()
    {
        var uow = new TestUnitOfWork();
        var saleRepo = new TestSaleRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();

        var variant = new ProductVariant
        {
            ProductVariantId = 50,
            VariantName = "Pin 21700",
            SKU = "PIN-21700",
            SellingPrice = 90000m,
            CostPrice = 60000m,
            WarrantyMonths = 12
        };
        varRepo.Add(variant);

        //OnHand = 10, Reserved = 8 => Available = 2
        invRepo.Add(new Inventory
        {
            ProductVariantId = 50,
            QuantityOnHand = 10,
            QuantityReserved = 8,
            Version = 1
        });

        var service = new PosService(saleRepo, invRepo, varRepo, uow);

        // Cashier sells exactly 2 (permitted!)
        var dto = new CreateSaleDto
        {
            PaymentMethod = "Cash",
            DiscountAmount = 10000m,
            Items = new List<CreateSaleItemDto>
            {
                new() { ProductVariantId = 50, Quantity = 2 }
            }
        };

        var result = await service.CreateSaleAsync(dto, cashierUserId: 5);

        // 1. Sale entity checks
        Assert.NotNull(result);
        Assert.True(result.SaleId > 0);
        Assert.Equal(180000m, result.Subtotal); // 2 * 90000
        Assert.Equal(10000m, result.DiscountAmount);
        Assert.Equal(170000m, result.TotalAmount);

        // 2. Physical stock decremented, reserved stock untouched (invariant 8 <= 8 holds!)
        var inv = await invRepo.GetByVariantIdAsync(50);
        Assert.NotNull(inv);
        Assert.Equal(8, inv.QuantityOnHand);
        Assert.Equal(8, inv.QuantityReserved);
        Assert.Equal(2, inv.Version);

        // 3. ISSUE-06: Real sale identity populated in CounterSale ledger transaction
        Assert.Single(invRepo.Transactions);
        var tx = invRepo.Transactions.First();
        Assert.Equal(InventoryTransactionType.CounterSale, tx.TransactionType);
        Assert.Equal("Sale", tx.ReferenceType);
        Assert.Equal(result.SaleId, tx.ReferenceId); // Must match SaleId!
        Assert.True(tx.ReferenceId > 0, "ReferenceId must not be 0!");
        Assert.Equal(-2, tx.QuantityChange);
        Assert.Equal(60000m, tx.UnitCost);
        Assert.Equal(5, tx.CreatedByUserId);

        Assert.True(uow.IsCommitted);
    }

    [Theory]
    [InlineData(0, 50, 1)]            // Empty items list handled via separate test
    [InlineData(10000, 50, 0)]        // Quantity <= 0
    [InlineData(10000, 50, -3)]       // Negative quantity
    [InlineData(10000, 0, 2)]         // ProductVariantId <= 0
    [InlineData(10000, -1, 2)]        // Negative ProductVariantId
    [InlineData(-5000, 50, 2)]        // Negative discount amount
    public async Task R4_PosSale_CartAndDiscountValidation_RejectsInvalidPayloads(decimal discount, long variantId, int qty)
    {
        var uow = new TestUnitOfWork();
        var saleRepo = new TestSaleRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();
        var service = new PosService(saleRepo, invRepo, varRepo, uow);

        var dto = new CreateSaleDto
        {
            PaymentMethod = "Cash",
            DiscountAmount = discount,
            Items = new List<CreateSaleItemDto>
            {
                new() { ProductVariantId = variantId, Quantity = qty }
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateSaleAsync(dto, cashierUserId: 1));
    }

    [Fact]
    public async Task R4_PosSale_DiscountExceedsSubtotal_ThrowsException()
    {
        var uow = new TestUnitOfWork();
        var saleRepo = new TestSaleRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();

        var variant = new ProductVariant { ProductVariantId = 50, SellingPrice = 50000m };
        varRepo.Add(variant);
        invRepo.Add(new Inventory { ProductVariantId = 50, QuantityOnHand = 10, QuantityReserved = 0 });

        var service = new PosService(saleRepo, invRepo, varRepo, uow);

        var dto = new CreateSaleDto
        {
            PaymentMethod = "Cash",
            DiscountAmount = 100000m, // Subtotal is 1 * 50,000 = 50,000 => Discount 100,000 > 50,000
            Items = new List<CreateSaleItemDto>
            {
                new() { ProductVariantId = 50, Quantity = 1 }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateSaleAsync(dto, cashierUserId: 1));
        Assert.Contains("không được vượt quá tổng tiền hàng", ex.Message);
        Assert.True(uow.IsRolledBack);
    }

    [Fact]
    public async Task R4_PosSale_MultipleItemsSameVariant_ValidatesCombinedQuantity()
    {
        var uow = new TestUnitOfWork();
        var saleRepo = new TestSaleRepository();
        var invRepo = new TestInventoryRepository();
        var varRepo = new TestVariantRepository();

        var variant = new ProductVariant { ProductVariantId = 50, SellingPrice = 50000m };
        varRepo.Add(variant);
        //OnHand = 10, Reserved = 6 => Available = 4
        invRepo.Add(new Inventory { ProductVariantId = 50, QuantityOnHand = 10, QuantityReserved = 6, Version = 1 });

        var service = new PosService(saleRepo, invRepo, varRepo, uow);

        // Cart has 2 items for variant 50: qty 2 and qty 3 = 5 (> Available 4)
        var dto = new CreateSaleDto
        {
            PaymentMethod = "Cash",
            Items = new List<CreateSaleItemDto>
            {
                new() { ProductVariantId = 50, Quantity = 2 },
                new() { ProductVariantId = 50, Quantity = 3 }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateSaleAsync(dto, cashierUserId: 1));
        Assert.Contains("không đủ tồn kho khả dụng", ex.Message);
        Assert.True(uow.IsRolledBack);
    }

    // =========================================================================
    // Test Fakes & Mock Infrastructure
    // =========================================================================

    private class TestUnitOfWork : IUnitOfWork
    {
        public bool IsCommitted { get; private set; }
        public bool IsRolledBack { get; private set; }
        public int SaveChangesCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return Task.FromResult(1);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            IsCommitted = false;
            IsRolledBack = false;
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            IsCommitted = true;
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            IsRolledBack = true;
            return Task.CompletedTask;
        }
    }

    private class TestVariantRepository : IGenericRepository<ProductVariant>
    {
        private readonly Dictionary<long, ProductVariant> _variants = new();
        public readonly List<ProductVariant> UpdatedVariants = new();

        public void Add(ProductVariant variant) => _variants[variant.ProductVariantId] = variant;

        public Task<ProductVariant?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
        {
            if (id is long lid && _variants.TryGetValue(lid, out var v)) return Task.FromResult<ProductVariant?>(v);
            return Task.FromResult<ProductVariant?>(null);
        }

        public Task<IReadOnlyList<ProductVariant>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductVariant>>(_variants.Values.ToList());

        public Task<IReadOnlyList<ProductVariant>> FindAsync(Expression<Func<ProductVariant, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductVariant>>(_variants.Values.AsQueryable().Where(predicate).ToList());

        public Task AddAsync(ProductVariant entity, CancellationToken cancellationToken = default)
        {
            _variants[entity.ProductVariantId] = entity;
            return Task.CompletedTask;
        }

        public void Update(ProductVariant entity)
        {
            _variants[entity.ProductVariantId] = entity;
            UpdatedVariants.Add(entity);
        }

        public void Remove(ProductVariant entity) => _variants.Remove(entity.ProductVariantId);
    }

    private class TestInventoryRepository : IInventoryRepository
    {
        private readonly Dictionary<long, Inventory> _inventory = new();
        public readonly List<InventoryTransaction> Transactions = new();

        public void Add(Inventory inv) => _inventory[inv.ProductVariantId] = inv;

        public Task<Inventory?> GetByVariantIdAsync(long variantId, CancellationToken cancellationToken = default)
        {
            _inventory.TryGetValue(variantId, out var inv);
            return Task.FromResult(inv);
        }

        public Task AddTransactionAsync(InventoryTransaction transaction, CancellationToken cancellationToken = default)
        {
            Transactions.Add(transaction);
            return Task.CompletedTask;
        }

        public Task<Inventory?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
        {
            if (id is long lid && _inventory.TryGetValue(lid, out var inv)) return Task.FromResult<Inventory?>(inv);
            return Task.FromResult<Inventory?>(null);
        }

        public Task<IReadOnlyList<Inventory>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Inventory>>(_inventory.Values.ToList());

        public Task<IReadOnlyList<Inventory>> FindAsync(Expression<Func<Inventory, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Inventory>>(_inventory.Values.AsQueryable().Where(predicate).ToList());

        public Task AddAsync(Inventory entity, CancellationToken cancellationToken = default)
        {
            _inventory[entity.ProductVariantId] = entity;
            return Task.CompletedTask;
        }

        public void Update(Inventory entity) => _inventory[entity.ProductVariantId] = entity;
        public void Remove(Inventory entity) => _inventory.Remove(entity.ProductVariantId);
    }

    private class TestAssemblyOrderRepository : IAssemblyOrderRepository
    {
        private readonly Dictionary<long, AssemblyOrder> _orders = new();
        private long _autoId = 1000;

        public Task<AssemblyOrder?> GetByCodeAsync(string assemblyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(_orders.Values.FirstOrDefault(o => o.AssemblyCode == assemblyCode));

        public Task<AssemblyOrder?> GetWithItemsAsync(long assemblyOrderId, CancellationToken cancellationToken = default)
        {
            _orders.TryGetValue(assemblyOrderId, out var o);
            return Task.FromResult(o);
        }

        public Task<AssemblyOrder?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
        {
            if (id is long lid && _orders.TryGetValue(lid, out var o)) return Task.FromResult<AssemblyOrder?>(o);
            return Task.FromResult<AssemblyOrder?>(null);
        }

        public Task<IReadOnlyList<AssemblyOrder>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AssemblyOrder>>(_orders.Values.ToList());

        public Task<IReadOnlyList<AssemblyOrder>> FindAsync(Expression<Func<AssemblyOrder, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AssemblyOrder>>(_orders.Values.AsQueryable().Where(predicate).ToList());

        public Task AddAsync(AssemblyOrder entity, CancellationToken cancellationToken = default)
        {
            if (entity.AssemblyOrderId == 0)
            {
                entity.AssemblyOrderId = ++_autoId;
            }
            _orders[entity.AssemblyOrderId] = entity;
            return Task.CompletedTask;
        }

        public void Update(AssemblyOrder entity) => _orders[entity.AssemblyOrderId] = entity;
        public void Remove(AssemblyOrder entity) => _orders.Remove(entity.AssemblyOrderId);
    }

    private class TestOrderRepository : IOrderRepository
    {
        private readonly Dictionary<long, Order> _orders = new();

        public void Add(Order order) => _orders[order.OrderId] = order;

        public Task<Order?> GetByCodeAsync(string orderCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(_orders.Values.FirstOrDefault(o => o.OrderCode == orderCode));

        public Task<Order?> GetWithDetailsAsync(long orderId, CancellationToken cancellationToken = default)
        {
            _orders.TryGetValue(orderId, out var o);
            return Task.FromResult(o);
        }

        public Task<Order?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
        {
            if (id is long lid && _orders.TryGetValue(lid, out var o)) return Task.FromResult<Order?>(o);
            return Task.FromResult<Order?>(null);
        }

        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>(_orders.Values.ToList());

        public Task<IReadOnlyList<Order>> FindAsync(Expression<Func<Order, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>(_orders.Values.AsQueryable().Where(predicate).ToList());

        public Task AddAsync(Order entity, CancellationToken cancellationToken = default)
        {
            _orders[entity.OrderId] = entity;
            return Task.CompletedTask;
        }

        public void Update(Order entity) => _orders[entity.OrderId] = entity;
        public void Remove(Order entity) => _orders.Remove(entity.OrderId);
    }

    private class TestSaleRepository : ISaleRepository
    {
        private readonly Dictionary<long, Sale> _sales = new();
        private long _autoId = 2000;

        public Task<Sale?> GetByCodeAsync(string saleCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(_sales.Values.FirstOrDefault(s => s.SaleCode == saleCode));

        public Task<Sale?> GetWithItemsAsync(long saleId, CancellationToken cancellationToken = default)
        {
            _sales.TryGetValue(saleId, out var s);
            return Task.FromResult(s);
        }

        public Task<Sale?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
        {
            if (id is long lid && _sales.TryGetValue(lid, out var s)) return Task.FromResult<Sale?>(s);
            return Task.FromResult<Sale?>(null);
        }

        public Task<IReadOnlyList<Sale>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Sale>>(_sales.Values.ToList());

        public Task<IReadOnlyList<Sale>> FindAsync(Expression<Func<Sale, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Sale>>(_sales.Values.AsQueryable().Where(predicate).ToList());

        public Task AddAsync(Sale entity, CancellationToken cancellationToken = default)
        {
            if (entity.SaleId == 0)
            {
                entity.SaleId = ++_autoId;
            }
            _sales[entity.SaleId] = entity;
            return Task.CompletedTask;
        }

        public void Update(Sale entity) => _sales[entity.SaleId] = entity;
        public void Remove(Sale entity) => _sales.Remove(entity.SaleId);
    }
}
