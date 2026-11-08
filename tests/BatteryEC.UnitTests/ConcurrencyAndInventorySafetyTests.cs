using System.Linq.Expressions;
using BatteryEC.Application.DTOs.Inventory;
using BatteryEC.Application.DTOs.Orders;
using BatteryEC.Application.Services;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Entities.Sales;
using BatteryEC.Domain.Entities.Services;
using BatteryEC.Domain.Repositories;
using BatteryEC.Infrastructure.Persistence;
using BatteryEC.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BatteryEC.UnitTests;

/// <summary>
/// Comprehensive verification test suite for Milestone 2:
/// - P0.5: InventoryService Quantity Validation & Existence
/// - P0.6: Concurrency and Atomicity in CreateOrderAsync (EF ConcurrencyToken, stock check, Version bump)
/// - P0.7: Transaction Safety & Rollback Guarantees across all early exit and failure branches
/// </summary>
public class ConcurrencyAndInventorySafetyTests
{
    // =========================================================================
    // SECTION 1: InventoryService Quantity Validation & Existence (P0.5, P0.7)
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task ReserveStockAsync_QuantityZeroOrNegative_ReturnsFalseAndDoesNotModifyStock(int invalidQuantity)
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 50,
            QuantityReserved = 5,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.ReserveStockAsync(10, invalidQuantity);

        Assert.False(result);
        Assert.Equal(5, inventory.QuantityReserved);
        Assert.Equal(1, inventory.Version);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task ReserveStockAsync_InventoryNotFound_ReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.ReserveStockAsync(999, 5);

        Assert.False(result);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task ReserveStockAsync_AvailableStockInsufficient_ReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 10,
            QuantityReserved = 8, // AvailableStock = 2
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.ReserveStockAsync(10, 5);

        Assert.False(result);
        Assert.Equal(8, inventory.QuantityReserved);
        Assert.Equal(1, inventory.Version);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task ReserveStockAsync_ValidQuantity_IncrementsReservedAndVersionAndReturnsTrue()
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 20,
            QuantityReserved = 2,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.ReserveStockAsync(10, 5);

        Assert.True(result);
        Assert.Equal(7, inventory.QuantityReserved);
        Assert.Equal(2, inventory.Version);
        Assert.Equal(1, uow.SaveCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    public async Task ReleaseStockAsync_QuantityZeroOrNegative_ReturnsFalseAndDoesNotModifyStock(int invalidQuantity)
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 20,
            QuantityReserved = 10,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.ReleaseStockAsync(10, invalidQuantity);

        Assert.False(result);
        Assert.Equal(10, inventory.QuantityReserved);
        Assert.Equal(1, inventory.Version);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task ReleaseStockAsync_InventoryNotFound_ReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.ReleaseStockAsync(999, 5);

        Assert.False(result);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task ReleaseStockAsync_ValidQuantity_DecrementsReservedClampsAtZeroAndBumpsVersion()
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 20,
            QuantityReserved = 4,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        // Release 10 units when only 4 reserved -> should clamp to 0
        var result = await service.ReleaseStockAsync(10, 10);

        Assert.True(result);
        Assert.Equal(0, inventory.QuantityReserved);
        Assert.Equal(2, inventory.Version);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task AdjustStockAsync_NullDto_RollsBackAndReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.AdjustStockAsync(null!, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AdjustStockAsync_ProductVariantIdNonPositive_RollsBackAndReturnsFalse(long invalidVariantId)
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = invalidVariantId,
            QuantityChange = 10,
            Reason = "Test"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task AdjustStockAsync_QuantityChangeZero_RollsBackAndReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 10, SKU = "SKU-10" });

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 10,
            QuantityChange = 0,
            Reason = "No change"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task AdjustStockAsync_VariantDoesNotExist_RollsBackAndReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository(); // empty - variant does not exist

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 999,
            QuantityChange = 10,
            Reason = "Audit"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task AdjustStockAsync_InventoryNotFoundAndQuantityChangeNegative_RollsBackAndReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 10, SKU = "SKU-10" });

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 10,
            QuantityChange = -5,
            Reason = "Deduct initial non-existent"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task AdjustStockAsync_ReduceBelowReservedStock_RollsBackAndReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 10, SKU = "SKU-10" });
        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 20,
            QuantityReserved = 15, // cannot drop below 15
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 10,
            QuantityChange = -10, // 20 - 10 = 10 < 15
            Reason = "Over-deduction"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.Equal(20, inventory.QuantityOnHand);
    }

    [Fact]
    public async Task AdjustStockAsync_ValidAdjustment_BumpsVersionCommitsAndReturnsTrue()
    {
        var uow = new FakeUnitOfWork();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 10, SKU = "SKU-10" });
        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 20,
            QuantityReserved = 5,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 10,
            QuantityChange = 15,
            Reason = "Supplier arrival"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.True(result);
        Assert.Equal(35, inventory.QuantityOnHand);
        Assert.Equal(2, inventory.Version);
        Assert.True(uow.CommitCalled);
        Assert.False(uow.RollbackCalled);
    }

    // =========================================================================
    // SECTION 2: OrderService Concurrency & Atomicity (P0.6, P0.7)
    // =========================================================================

    [Fact]
    public async Task CreateOrderAsync_NullDto_ThrowsArgumentNullException()
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateOrderAsync(null!));
    }

    [Fact]
    public async Task CreateOrderAsync_EmptyItems_ThrowsInvalidOperationException()
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var dto = new CreateOrderDto
        {
            UserId = 1,
            RecipientName = "Alice",
            Items = new List<CreateOrderItemDto>()
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(dto));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task CreateOrderAsync_ItemQuantityNonPositive_RollsBackAndThrowsInvalidOperationException(int invalidQuantity)
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var dto = new CreateOrderDto
        {
            UserId = 1,
            RecipientName = "Alice",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductVariantId = 10, Quantity = invalidQuantity }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(dto));
        Assert.Contains("positive", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task CreateOrderAsync_InventoryNotFound_RollsBackAndThrowsInvalidOperationException()
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 10, SKU = "SKU-10", SellingPrice = 100m });
        // No inventory record for variant 10

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var dto = new CreateOrderDto
        {
            UserId = 1,
            RecipientName = "Alice",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductVariantId = 10, Quantity = 2 }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(dto));
        Assert.Contains("Insufficient", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task CreateOrderAsync_AvailableStockInsufficient_RollsBackAndThrowsInvalidOperationException()
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 10, SKU = "SKU-10", SellingPrice = 100m });
        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 10,
            QuantityReserved = 8, // Available = 2
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var dto = new CreateOrderDto
        {
            UserId = 1,
            RecipientName = "Alice",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductVariantId = 10, Quantity = 5 } // Requested 5 > Available 2
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(dto));
        Assert.Contains("Insufficient", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.Equal(8, inventory.QuantityReserved); // Unchanged
    }

    [Fact]
    public async Task CreateOrderAsync_SufficientStock_BumpsVersionCommitsAndReturnsOrder()
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 10, SKU = "SKU-10", SellingPrice = 100m });
        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 20,
            QuantityReserved = 2, // Available = 18
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var dto = new CreateOrderDto
        {
            UserId = 1,
            RecipientName = "Alice",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductVariantId = 10, Quantity = 5 }
            }
        };

        var result = await service.CreateOrderAsync(dto);

        Assert.NotNull(result);
        Assert.Equal(7, inventory.QuantityReserved); // 2 + 5
        Assert.Equal(2, inventory.Version); // Version bumped
        Assert.True(uow.CommitCalled);
        Assert.False(uow.RollbackCalled);
    }

    [Fact]
    public async Task CreateOrderAsync_DbUpdateConcurrencyException_RollsBackAndThrowsFriendlyConflictMessage()
    {
        var uow = new FakeUnitOfWork
        {
            ThrowOnCommit = new DbUpdateConcurrencyException("Optimistic concurrency violation")
        };
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 10, SKU = "SKU-10", SellingPrice = 100m });
        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 20,
            QuantityReserved = 2,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var dto = new CreateOrderDto
        {
            UserId = 1,
            RecipientName = "Alice",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductVariantId = 10, Quantity = 2 }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(dto));
        Assert.Contains("Stock conflict detected, please retry", ex.Message);
        Assert.True(uow.RollbackCalled);
    }

    [Fact]
    public async Task CompleteOrderAsync_OrderNotFound_RollsBackAndReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CompleteOrderAsync(999);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Theory]
    [InlineData("PendingConfirmation")]
    [InlineData("Confirmed")]
    [InlineData("Processing")]
    [InlineData("Cancelled")]
    public async Task CompleteOrderAsync_StatusNotShipped_RollsBackAndReturnsFalse(string invalidStatus)
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var order = new Order
        {
            OrderId = 1,
            OrderStatus = invalidStatus
        };
        orderRepo.AddOrder(order);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CompleteOrderAsync(1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task CompleteOrderAsync_ValidShippedOrder_DeductsStockBumpsVersionCommitsAndReturnsTrue()
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 20,
            QuantityReserved = 5,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var order = new Order
        {
            OrderId = 1,
            OrderStatus = "Shipped",
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 10, Quantity = 5, UnitPrice = 100m, LineTotal = 500m }
            }
        };
        orderRepo.AddOrder(order);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CompleteOrderAsync(1);

        Assert.True(result);
        Assert.Equal("Completed", order.OrderStatus);
        Assert.Equal(15, inventory.QuantityOnHand); // 20 - 5
        Assert.Equal(0, inventory.QuantityReserved); // 5 - 5
        Assert.Equal(2, inventory.Version); // Version bumped
        Assert.True(uow.CommitCalled);
        Assert.False(uow.RollbackCalled);
    }

    [Fact]
    public async Task CancelOrderAsync_OrderNotFound_RollsBackAndReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CancelOrderAsync(999);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Completed")]
    public async Task CancelOrderAsync_OrderAlreadyClosed_RollsBackAndReturnsFalse(string closedStatus)
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var order = new Order
        {
            OrderId = 1,
            OrderStatus = closedStatus
        };
        orderRepo.AddOrder(order);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CancelOrderAsync(1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task CancelOrderAsync_ValidOrder_ReleasesReservedStockBumpsVersionCommitsAndReturnsTrue()
    {
        var uow = new FakeUnitOfWork();
        var orderRepo = new FakeOrderRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 20,
            QuantityReserved = 5,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var order = new Order
        {
            OrderId = 1,
            OrderStatus = "PendingConfirmation",
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 10, Quantity = 5, UnitPrice = 100m, LineTotal = 500m }
            }
        };
        orderRepo.AddOrder(order);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CancelOrderAsync(1);

        Assert.True(result);
        Assert.Equal("Cancelled", order.OrderStatus);
        Assert.Equal(0, inventory.QuantityReserved); // 5 - 5
        Assert.Equal(2, inventory.Version); // Version bumped
        Assert.True(uow.CommitCalled);
        Assert.False(uow.RollbackCalled);
    }

    // =========================================================================
    // SECTION 3: RepairService Transaction Safety (P0.7)
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AddRepairItemAsync_QuantityZeroOrNegative_RollsBackAndReturnsFalse(int invalidQuantity)
    {
        var uow = new FakeUnitOfWork();
        var ticketRepo = new FakeRepairTicketRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(1, 10, invalidQuantity, 50m, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task AddRepairItemAsync_TicketNotFound_RollsBackAndReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var ticketRepo = new FakeRepairTicketRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(999, 10, 2, 50m, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Delivered")]
    public async Task AddRepairItemAsync_TicketCompletedOrDelivered_RollsBackAndReturnsFalse(string closedStatus)
    {
        var uow = new FakeUnitOfWork();
        var ticketRepo = new FakeRepairTicketRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var ticket = new RepairTicket
        {
            RepairTicketId = 1,
            TicketStatus = closedStatus
        };
        ticketRepo.AddTicket(ticket);

        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(1, 10, 2, 50m, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task AddRepairItemAsync_VariantNotFound_RollsBackAndReturnsFalse()
    {
        var uow = new FakeUnitOfWork();
        var ticketRepo = new FakeRepairTicketRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var ticket = new RepairTicket
        {
            RepairTicketId = 1,
            TicketStatus = "UnderRepair"
        };
        ticketRepo.AddTicket(ticket);

        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(1, 999, 2, 50m, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task AddRepairItemAsync_ValidTicketAndVariant_DeductsStockBumpsVersionCommitsAndReturnsTrue()
    {
        var uow = new FakeUnitOfWork();
        var ticketRepo = new FakeRepairTicketRepository();
        var inventoryRepo = new FakeInventoryRepository();
        var variantRepo = new FakeVariantRepository();

        var ticket = new RepairTicket
        {
            RepairTicketId = 1,
            TicketStatus = "UnderRepair",
            FinalCost = 100m
        };
        ticketRepo.AddTicket(ticket);

        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 10, SKU = "BATT-01", CostPrice = 30m });

        var inventory = new Inventory
        {
            ProductVariantId = 10,
            QuantityOnHand = 20,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(1, 10, 2, 50m, 1);

        Assert.True(result);
        Assert.Equal(200m, ticket.FinalCost); // 100 + (2 * 50)
        Assert.Equal(18, inventory.QuantityOnHand); // 20 - 2
        Assert.Equal(2, inventory.Version); // Version bumped
        Assert.True(uow.CommitCalled);
        Assert.False(uow.RollbackCalled);
    }

    // =========================================================================
    // SECTION 4: EF Core Concurrency Token Metadata Verification (P0.6)
    // =========================================================================

    [Fact]
    public void InventoryConfiguration_VersionProperty_IsConfiguredAsConcurrencyToken()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=battery_ec_test;Username=postgres;Password=postgres")
                      .UseSnakeCaseNamingConvention();

        using var context = new BatteryDbContext(optionsBuilder.Options);

        var inventoryEntityType = context.Model.FindEntityType(typeof(Inventory));
        Assert.NotNull(inventoryEntityType);

        var versionProperty = inventoryEntityType.FindProperty(nameof(Inventory.Version));
        Assert.NotNull(versionProperty);
        Assert.True(versionProperty.IsConcurrencyToken, "Inventory.Version must be marked as IsConcurrencyToken for optimistic concurrency.");
    }

    // =========================================================================
    // TEST DOUBLES / FAKES
    // =========================================================================

    private class FakeUnitOfWork : IUnitOfWork
    {
        public bool BeginCalled { get; private set; }
        public bool CommitCalled { get; private set; }
        public bool RollbackCalled { get; private set; }
        public int SaveCount { get; private set; }
        public Exception? ThrowOnCommit { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            BeginCalled = true;
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            CommitCalled = true;
            if (ThrowOnCommit != null)
            {
                throw ThrowOnCommit;
            }
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            RollbackCalled = true;
            return Task.CompletedTask;
        }
    }

    private class FakeInventoryRepository : IInventoryRepository
    {
        private readonly Dictionary<long, Inventory> _inventories = new();
        public readonly List<InventoryTransaction> Transactions = new();

        public void AddInventory(Inventory inventory) => _inventories[inventory.ProductVariantId] = inventory;

        public Task<Inventory?> GetByVariantIdAsync(long variantId, CancellationToken cancellationToken = default)
        {
            _inventories.TryGetValue(variantId, out var inv);
            return Task.FromResult(inv);
        }

        public Task AddTransactionAsync(InventoryTransaction transaction, CancellationToken cancellationToken = default)
        {
            Transactions.Add(transaction);
            return Task.CompletedTask;
        }

        public Task<Inventory?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
        {
            if (id is long lid) return GetByVariantIdAsync(lid, cancellationToken);
            return Task.FromResult<Inventory?>(null);
        }
        public Task<IReadOnlyList<Inventory>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Inventory>>(_inventories.Values.ToList());
        public Task<IReadOnlyList<Inventory>> FindAsync(Expression<Func<Inventory, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Inventory>>(_inventories.Values.AsQueryable().Where(predicate).ToList());
        public Task AddAsync(Inventory entity, CancellationToken cancellationToken = default)
        {
            _inventories[entity.ProductVariantId] = entity;
            return Task.CompletedTask;
        }
        public void Update(Inventory entity) => _inventories[entity.ProductVariantId] = entity;
        public void Remove(Inventory entity) => _inventories.Remove(entity.ProductVariantId);
    }

    private class FakeVariantRepository : IGenericRepository<ProductVariant>
    {
        private readonly Dictionary<long, ProductVariant> _variants = new();

        public void AddVariant(ProductVariant variant) => _variants[variant.ProductVariantId] = variant;

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

        public void Update(ProductVariant entity) => _variants[entity.ProductVariantId] = entity;
        public void Remove(ProductVariant entity) => _variants.Remove(entity.ProductVariantId);
    }

    private class FakeOrderRepository : IOrderRepository
    {
        private readonly Dictionary<long, Order> _orders = new();

        public void AddOrder(Order order) => _orders[order.OrderId] = order;

        public Task<Order?> GetByCodeAsync(string orderCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(_orders.Values.FirstOrDefault(o => o.OrderCode == orderCode));

        public Task<Order?> GetWithDetailsAsync(long orderId, CancellationToken cancellationToken = default)
        {
            _orders.TryGetValue(orderId, out var o);
            return Task.FromResult(o);
        }

        public Task<IReadOnlyList<Order>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>(_orders.Values.Where(o => o.UserId == userId).ToList());

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

    private class FakeRepairTicketRepository : IRepairTicketRepository
    {
        private readonly Dictionary<long, RepairTicket> _tickets = new();

        public void AddTicket(RepairTicket ticket) => _tickets[ticket.RepairTicketId] = ticket;

        public Task<RepairTicket?> GetByCodeAsync(string ticketCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tickets.Values.FirstOrDefault(t => t.TicketCode == ticketCode));

        public Task<RepairTicket?> GetWithItemsAsync(long ticketId, CancellationToken cancellationToken = default)
        {
            _tickets.TryGetValue(ticketId, out var t);
            return Task.FromResult(t);
        }

        public Task<IReadOnlyList<RepairTicket>> GetByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepairTicket>>(_tickets.Values.Where(t => t.CustomerId == customerId).ToList());

        public Task<RepairTicket?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
        {
            if (id is long lid && _tickets.TryGetValue(lid, out var t)) return Task.FromResult<RepairTicket?>(t);
            return Task.FromResult<RepairTicket?>(null);
        }

        public Task<IReadOnlyList<RepairTicket>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepairTicket>>(_tickets.Values.ToList());

        public Task<IReadOnlyList<RepairTicket>> FindAsync(Expression<Func<RepairTicket, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepairTicket>>(_tickets.Values.AsQueryable().Where(predicate).ToList());

        public Task AddAsync(RepairTicket entity, CancellationToken cancellationToken = default)
        {
            _tickets[entity.RepairTicketId] = entity;
            return Task.CompletedTask;
        }

        public void Update(RepairTicket entity) => _tickets[entity.RepairTicketId] = entity;
        public void Remove(RepairTicket entity) => _tickets.Remove(entity.RepairTicketId);
    }
}
