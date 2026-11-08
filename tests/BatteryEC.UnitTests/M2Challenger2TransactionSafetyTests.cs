using System.Linq.Expressions;
using BatteryEC.Application.DTOs.Inventory;
using BatteryEC.Application.DTOs.Orders;
using BatteryEC.Application.DTOs.Repairs;
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
/// Milestone 2 Challenger 2: Adversarial & Empirical Verification of Transaction Safety & Rollback Guarantees (P0.7).
/// Rigorously verifies:
/// 1. InventoryService.AdjustStockAsync early failure branches (nonexistent variant, invalid quantity, reduction below reserved, invalid DTO)
/// 2. OrderService.CompleteOrderAsync early failure branches (nonexistent order, wrong status)
/// 3. OrderService.CancelOrderAsync early failure branches (nonexistent order, already completed/cancelled)
/// 4. RepairService.AddRepairItemAsync early failure branches (nonexistent ticket, wrong ticket status, nonexistent variant, invalid quantity)
/// 5. In EVERY failure branch, _unitOfWork.HasActiveTransaction is verified to be false immediately after return/throw
/// 6. Real UnitOfWork lifecycle on live PostgreSQL (Dispose/Rollback leaves 0 dangling transactions)
/// </summary>
public class M2Challenger2TransactionSafetyTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=battery_ec;Username=postgres;Password=postgres";

    // =========================================================================
    // 1. InventoryService.AdjustStockAsync Early Exit Branches
    // =========================================================================

    [Fact]
    public async Task AdjustStockAsync_NullDto_RollsBackAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();
        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.AdjustStockAsync(null!, 1);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public async Task AdjustStockAsync_NonPositiveVariantId_RollsBackAndHasNoActiveTransaction(long invalidVariantId)
    {
        var uow = new ObservableUnitOfWork();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();
        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = invalidVariantId,
            QuantityChange = 10,
            Reason = "Adversarial test nonpositive variant"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
    }

    [Fact]
    public async Task AdjustStockAsync_ZeroQuantityChange_RollsBackAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();
        variantRepo.Add(new ProductVariant { ProductVariantId = 101, SKU = "BATT-101" });
        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 101,
            QuantityChange = 0, // Invalid: zero change
            Reason = "No-op adjustment"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
    }

    [Fact]
    public async Task AdjustStockAsync_NonexistentVariant_RollsBackAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository(); // Empty repo
        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 88888, // Variant does not exist in DB
            QuantityChange = 10,
            Reason = "Adjust nonexistent variant"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
    }

    [Fact]
    public async Task AdjustStockAsync_NonexistentInventoryWithNegativeQuantity_RollsBackAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var inventoryRepo = new InMemoryInventoryRepository(); // No inventory
        var variantRepo = new InMemoryVariantRepository();
        variantRepo.Add(new ProductVariant { ProductVariantId = 202, SKU = "BATT-202" });
        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 202,
            QuantityChange = -5, // Cannot reduce below zero when inventory doesn't exist
            Reason = "Negative stock for non-existent inventory"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
    }

    [Fact]
    public async Task AdjustStockAsync_ReductionBelowReservedStock_RollsBackAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();

        variantRepo.Add(new ProductVariant { ProductVariantId = 303, SKU = "BATT-303" });
        var existingInventory = new Inventory
        {
            ProductVariantId = 303,
            QuantityOnHand = 20,
            QuantityReserved = 15, // Available = 5
            Version = 1
        };
        inventoryRepo.Add(existingInventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 303,
            QuantityChange = -10, // 20 - 10 = 10 < QuantityReserved (15) -> ILLEGAL
            Reason = "Reduction below reserved"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
        Assert.Equal(20, existingInventory.QuantityOnHand); // Unmodified
    }

    [Fact]
    public async Task AdjustStockAsync_SuccessCase_CommitsAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();

        variantRepo.Add(new ProductVariant { ProductVariantId = 404, SKU = "BATT-404" });
        var existingInventory = new Inventory
        {
            ProductVariantId = 404,
            QuantityOnHand = 20,
            QuantityReserved = 5,
            Version = 1
        };
        inventoryRepo.Add(existingInventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 404,
            QuantityChange = 10,
            Reason = "Restock shipment"
        };

        var result = await service.AdjustStockAsync(dto, 1);

        Assert.True(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.CommitCalled);
        Assert.False(uow.RollbackCalled);
        Assert.False(uow.HasActiveTransaction);
        Assert.Equal(30, existingInventory.QuantityOnHand);
        Assert.Equal(2, existingInventory.Version);
    }

    // =========================================================================
    // 2. OrderService.CompleteOrderAsync Early Exit Branches
    // =========================================================================

    [Fact]
    public async Task CompleteOrderAsync_NonexistentOrder_RollsBackAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var orderRepo = new InMemoryOrderRepository(); // Empty
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();
        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CompleteOrderAsync(99999);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
    }

    [Theory]
    [InlineData("PendingConfirmation")]
    [InlineData("Confirmed")]
    [InlineData("Processing")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public async Task CompleteOrderAsync_WrongOrderStatus_RollsBackAndHasNoActiveTransaction(string invalidStatus)
    {
        var uow = new ObservableUnitOfWork();
        var orderRepo = new InMemoryOrderRepository();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();

        var order = new Order
        {
            OrderId = 501,
            OrderStatus = invalidStatus, // Must be "Shipped" to complete
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 1, Quantity = 2, UnitPrice = 100m, LineTotal = 200m }
            }
        };
        orderRepo.Add(order);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CompleteOrderAsync(501);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
        Assert.Equal(invalidStatus, order.OrderStatus); // Status intact
    }

    [Fact]
    public async Task CompleteOrderAsync_ValidShippedOrder_CommitsAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var orderRepo = new InMemoryOrderRepository();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 601,
            QuantityOnHand = 15,
            QuantityReserved = 3,
            Version = 1
        };
        inventoryRepo.Add(inventory);

        var order = new Order
        {
            OrderId = 601,
            OrderStatus = "Shipped",
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 601, Quantity = 3, UnitPrice = 200m, LineTotal = 600m }
            }
        };
        orderRepo.Add(order);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CompleteOrderAsync(601);

        Assert.True(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.CommitCalled);
        Assert.False(uow.RollbackCalled);
        Assert.False(uow.HasActiveTransaction);
        Assert.Equal("Completed", order.OrderStatus);
        Assert.Equal(12, inventory.QuantityOnHand);
        Assert.Equal(0, inventory.QuantityReserved);
        Assert.Equal(2, inventory.Version);
    }

    // =========================================================================
    // 3. OrderService.CancelOrderAsync Early Exit Branches
    // =========================================================================

    [Fact]
    public async Task CancelOrderAsync_NonexistentOrder_RollsBackAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var orderRepo = new InMemoryOrderRepository();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();
        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CancelOrderAsync(77777);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Completed")]
    public async Task CancelOrderAsync_AlreadyClosedOrder_RollsBackAndHasNoActiveTransaction(string closedStatus)
    {
        var uow = new ObservableUnitOfWork();
        var orderRepo = new InMemoryOrderRepository();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();

        var order = new Order
        {
            OrderId = 801,
            OrderStatus = closedStatus,
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 1, Quantity = 1 }
            }
        };
        orderRepo.Add(order);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CancelOrderAsync(801);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
        Assert.Equal(closedStatus, order.OrderStatus);
    }

    [Theory]
    [InlineData("PendingConfirmation")]
    [InlineData("Confirmed")]
    [InlineData("Processing")]
    public async Task CancelOrderAsync_OpenOrder_ReleasesStockCommitsAndHasNoActiveTransaction(string openStatus)
    {
        var uow = new ObservableUnitOfWork();
        var orderRepo = new InMemoryOrderRepository();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 901,
            QuantityOnHand = 20,
            QuantityReserved = 5,
            Version = 1
        };
        inventoryRepo.Add(inventory);

        var order = new Order
        {
            OrderId = 901,
            OrderStatus = openStatus,
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 901, Quantity = 5, UnitPrice = 100m, LineTotal = 500m }
            }
        };
        orderRepo.Add(order);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CancelOrderAsync(901);

        Assert.True(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.CommitCalled);
        Assert.False(uow.RollbackCalled);
        Assert.False(uow.HasActiveTransaction);
        Assert.Equal("Cancelled", order.OrderStatus);
        Assert.Equal(0, inventory.QuantityReserved);
        Assert.Equal(2, inventory.Version);
    }

    // =========================================================================
    // 4. RepairService.AddRepairItemAsync Early Exit Branches
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task AddRepairItemAsync_QuantityZeroOrNegative_RollsBackAndHasNoActiveTransaction(int invalidQuantity)
    {
        var uow = new ObservableUnitOfWork();
        var ticketRepo = new InMemoryRepairTicketRepository();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();
        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(1, 10, invalidQuantity, 50m, 1);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
    }

    [Fact]
    public async Task AddRepairItemAsync_NonexistentTicket_RollsBackAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var ticketRepo = new InMemoryRepairTicketRepository(); // Empty
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();
        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(99999, 10, 2, 50m, 1);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Delivered")]
    public async Task AddRepairItemAsync_WrongTicketStatus_RollsBackAndHasNoActiveTransaction(string closedStatus)
    {
        var uow = new ObservableUnitOfWork();
        var ticketRepo = new InMemoryRepairTicketRepository();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();

        var ticket = new RepairTicket
        {
            RepairTicketId = 1001,
            TicketStatus = closedStatus,
            FinalCost = 200m
        };
        ticketRepo.Add(ticket);

        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(1001, 10, 2, 50m, 1);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
        Assert.Empty(ticket.Items);
        Assert.Equal(200m, ticket.FinalCost); // Cost untouched
    }

    [Fact]
    public async Task AddRepairItemAsync_NonexistentVariant_RollsBackAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var ticketRepo = new InMemoryRepairTicketRepository();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository(); // Empty variants

        var ticket = new RepairTicket
        {
            RepairTicketId = 1002,
            TicketStatus = "UnderRepair",
            FinalCost = 50m
        };
        ticketRepo.Add(ticket);

        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(1002, 99999, 1, 30m, 1);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.False(uow.HasActiveTransaction);
        Assert.Empty(ticket.Items);
        Assert.Equal(50m, ticket.FinalCost);
    }

    [Fact]
    public async Task AddRepairItemAsync_ValidTicketAndVariant_CommitsAndHasNoActiveTransaction()
    {
        var uow = new ObservableUnitOfWork();
        var ticketRepo = new InMemoryRepairTicketRepository();
        var inventoryRepo = new InMemoryInventoryRepository();
        var variantRepo = new InMemoryVariantRepository();

        var ticket = new RepairTicket
        {
            RepairTicketId = 1003,
            TicketCode = "REP-TEST-1003",
            TicketStatus = "UnderRepair",
            FinalCost = 100m
        };
        ticketRepo.Add(ticket);

        variantRepo.Add(new ProductVariant { ProductVariantId = 1003, SKU = "PART-1003", CostPrice = 25m });

        var inventory = new Inventory
        {
            ProductVariantId = 1003,
            QuantityOnHand = 10,
            Version = 1
        };
        inventoryRepo.Add(inventory);

        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(1003, 1003, 2, 60m, 1);

        Assert.True(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.CommitCalled);
        Assert.False(uow.RollbackCalled);
        Assert.False(uow.HasActiveTransaction);
        Assert.Single(ticket.Items);
        Assert.Equal(220m, ticket.FinalCost); // 100 + (2 * 60)
        Assert.Equal(8, inventory.QuantityOnHand); // 10 - 2
        Assert.Equal(2, inventory.Version);
    }

    // =========================================================================
    // 5. Live PostgreSQL UnitOfWork Lifecycle & Zero Dangling Transactions
    // =========================================================================

    [Fact]
    public async Task RealUnitOfWork_BeginAndRollback_LeavesZeroActiveTransactionsOnDatabase()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString)
                      .UseSnakeCaseNamingConvention();

        using var context = new BatteryDbContext(optionsBuilder.Options);
        var uow = new UnitOfWork(context);

        Assert.Null(context.Database.CurrentTransaction);

        await uow.BeginTransactionAsync();
        Assert.NotNull(context.Database.CurrentTransaction);

        await uow.RollbackTransactionAsync();
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task RealUnitOfWork_BeginAndCommit_LeavesZeroActiveTransactionsOnDatabase()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString)
                      .UseSnakeCaseNamingConvention();

        using var context = new BatteryDbContext(optionsBuilder.Options);
        var uow = new UnitOfWork(context);

        Assert.Null(context.Database.CurrentTransaction);

        await uow.BeginTransactionAsync();
        Assert.NotNull(context.Database.CurrentTransaction);

        await uow.CommitTransactionAsync();
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task RealUnitOfWork_DisposeWithoutExplicitCommit_CleansUpDanglingTransaction()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString)
                      .UseSnakeCaseNamingConvention();

        using var context = new BatteryDbContext(optionsBuilder.Options);
        var uow = new UnitOfWork(context);

        await uow.BeginTransactionAsync();
        Assert.NotNull(context.Database.CurrentTransaction);

        // Scope disposal without commit/rollback
        uow.Dispose();
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task RealUnitOfWork_DisposeAsyncWithoutExplicitCommit_CleansUpDanglingTransaction()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString)
                      .UseSnakeCaseNamingConvention();

        using var context = new BatteryDbContext(optionsBuilder.Options);
        var uow = new UnitOfWork(context);

        await uow.BeginTransactionAsync();
        Assert.NotNull(context.Database.CurrentTransaction);

        // Async Scope disposal without commit/rollback
        await uow.DisposeAsync();
        Assert.Null(context.Database.CurrentTransaction);
    }

    // =========================================================================
    // Observable Test Double for UnitOfWork
    // =========================================================================

    public class ObservableUnitOfWork : IUnitOfWork
    {
        public bool HasActiveTransaction { get; private set; }
        public bool BeginCalled { get; private set; }
        public bool CommitCalled { get; private set; }
        public bool RollbackCalled { get; private set; }
        public int SaveChangesCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (HasActiveTransaction)
            {
                throw new InvalidOperationException("Transaction is already active.");
            }
            BeginCalled = true;
            HasActiveTransaction = true;
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (!HasActiveTransaction)
            {
                throw new InvalidOperationException("Cannot commit: No active transaction.");
            }
            CommitCalled = true;
            HasActiveTransaction = false;
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (!HasActiveTransaction)
            {
                throw new InvalidOperationException("Cannot rollback: No active transaction.");
            }
            RollbackCalled = true;
            HasActiveTransaction = false;
            return Task.CompletedTask;
        }
    }

    // =========================================================================
    // In-Memory Test Repositories
    // =========================================================================

    private class InMemoryInventoryRepository : IInventoryRepository
    {
        private readonly Dictionary<long, Inventory> _inventories = new();
        public readonly List<InventoryTransaction> Transactions = new();

        public void Add(Inventory inventory) => _inventories[inventory.ProductVariantId] = inventory;

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
            if (id is long lid && _inventories.TryGetValue(lid, out var inv)) return Task.FromResult<Inventory?>(inv);
            return Task.FromResult<Inventory?>(null);
        }

        public Task<IReadOnlyList<Inventory>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Inventory>>(_inventories.Values.ToList());

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

    private class InMemoryVariantRepository : IGenericRepository<ProductVariant>
    {
        private readonly Dictionary<long, ProductVariant> _variants = new();

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

        public void Update(ProductVariant entity) => _variants[entity.ProductVariantId] = entity;
        public void Remove(ProductVariant entity) => _variants.Remove(entity.ProductVariantId);
    }

    private class InMemoryOrderRepository : IOrderRepository
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

    private class InMemoryRepairTicketRepository : IRepairTicketRepository
    {
        private readonly Dictionary<long, RepairTicket> _tickets = new();

        public void Add(RepairTicket ticket) => _tickets[ticket.RepairTicketId] = ticket;

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
