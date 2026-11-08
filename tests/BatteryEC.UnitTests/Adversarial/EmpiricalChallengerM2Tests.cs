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

namespace BatteryEC.UnitTests.Adversarial;

/// <summary>
/// Adversarial challenger test suite independently developed by Milestone 2 Reviewer.
/// Tests stress conditions, boundary limits, transaction atomicity, rollbacks, and concurrency tokens.
/// </summary>
public class EmpiricalChallengerM2Tests
{
    // =========================================================================
    // 1. P0.5: InventoryService Edge Cases & Boundary Clamping
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    [InlineData(-99999)]
    [InlineData(int.MinValue)]
    public async Task ReserveStockAsync_ExtremeNonPositiveQuantities_RejectedSafely(int invalidQuantity)
    {
        var uow = new AdversarialUnitOfWork();
        var inventoryRepo = new AdversarialInventoryRepository();
        var variantRepo = new AdversarialVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 1,
            QuantityOnHand = 100,
            QuantityReserved = 10,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.ReserveStockAsync(1, invalidQuantity);

        Assert.False(result);
        Assert.Equal(10, inventory.QuantityReserved);
        Assert.Equal(1, inventory.Version);
        Assert.Equal(0, uow.SaveCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    [InlineData(-99999)]
    [InlineData(int.MinValue)]
    public async Task ReleaseStockAsync_ExtremeNonPositiveQuantities_RejectedSafely(int invalidQuantity)
    {
        var uow = new AdversarialUnitOfWork();
        var inventoryRepo = new AdversarialInventoryRepository();
        var variantRepo = new AdversarialVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 1,
            QuantityOnHand = 100,
            QuantityReserved = 20,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.ReleaseStockAsync(1, invalidQuantity);

        Assert.False(result);
        Assert.Equal(20, inventory.QuantityReserved);
        Assert.Equal(1, inventory.Version);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task ReleaseStockAsync_ReleaseMoreThanReserved_ClampsAtZeroWithoutUnderflow()
    {
        var uow = new AdversarialUnitOfWork();
        var inventoryRepo = new AdversarialInventoryRepository();
        var variantRepo = new AdversarialVariantRepository();

        var inventory = new Inventory
        {
            ProductVariantId = 1,
            QuantityOnHand = 100,
            QuantityReserved = 5,
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.ReleaseStockAsync(1, 50);

        Assert.True(result);
        Assert.Equal(0, inventory.QuantityReserved); // Clamped at 0, not negative
        Assert.Equal(2, inventory.Version);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task AdjustStockAsync_NonExistentVariant_RollsBackTransactionAndLeavesNoState()
    {
        var uow = new AdversarialUnitOfWork();
        var inventoryRepo = new AdversarialInventoryRepository();
        var variantRepo = new AdversarialVariantRepository();

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 404,
            QuantityChange = 15,
            Reason = "Ghost variant adjustment"
        };

        var result = await service.AdjustStockAsync(dto, 99);

        Assert.False(result);
        Assert.True(uow.BeginCalled);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task AdjustStockAsync_ReductionExceedingAvailableStock_RollsBackTransaction()
    {
        var uow = new AdversarialUnitOfWork();
        var inventoryRepo = new AdversarialInventoryRepository();
        var variantRepo = new AdversarialVariantRepository();

        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 1, SKU = "BATT-01" });
        var inventory = new Inventory
        {
            ProductVariantId = 1,
            QuantityOnHand = 50,
            QuantityReserved = 45, // Only 5 available
            Version = 1
        };
        inventoryRepo.AddInventory(inventory);

        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var dto = new StockAdjustmentDto
        {
            ProductVariantId = 1,
            QuantityChange = -10, // 50 - 10 = 40 < 45 reserved
            Reason = "Over-deduction attempt"
        };

        var result = await service.AdjustStockAsync(dto, 99);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.Equal(50, inventory.QuantityOnHand);
    }

    // =========================================================================
    // 2. P0.6 & P0.7: Multi-item Order Atomicity & Rollback Guarantees
    // =========================================================================

    [Fact]
    public async Task CreateOrderAsync_MultiItemOrder_SecondItemFailsStockCheck_EntireTransactionRolledBack()
    {
        var uow = new AdversarialUnitOfWork();
        var orderRepo = new AdversarialOrderRepository();
        var inventoryRepo = new AdversarialInventoryRepository();
        var variantRepo = new AdversarialVariantRepository();

        // Variant 1: Sufficient stock
        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 101, SKU = "V101", SellingPrice = 50m });
        var inv1 = new Inventory { ProductVariantId = 101, QuantityOnHand = 10, QuantityReserved = 0, Version = 1 };
        inventoryRepo.AddInventory(inv1);

        // Variant 2: Insufficient stock
        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 102, SKU = "V102", SellingPrice = 80m });
        var inv2 = new Inventory { ProductVariantId = 102, QuantityOnHand = 2, QuantityReserved = 2, Version = 1 }; // Available = 0
        inventoryRepo.AddInventory(inv2);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var dto = new CreateOrderDto
        {
            UserId = 1,
            RecipientName = "Bob",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductVariantId = 101, Quantity = 5 }, // Would succeed
                new() { ProductVariantId = 102, Quantity = 1 }  // Fails (0 available)
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(dto));
        Assert.Contains("Insufficient", ex.Message);

        // Transaction must be rolled back
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);

        // Crucial: No orders saved in order repository
        Assert.Empty(orderRepo.AllOrders);
    }

    [Fact]
    public async Task CreateOrderAsync_DbUpdateConcurrencyException_RollsBackAndThrowsFriendlyMessage()
    {
        var uow = new AdversarialUnitOfWork
        {
            ThrowOnCommit = new DbUpdateConcurrencyException("Concurrency race condition")
        };
        var orderRepo = new AdversarialOrderRepository();
        var inventoryRepo = new AdversarialInventoryRepository();
        var variantRepo = new AdversarialVariantRepository();

        variantRepo.AddVariant(new ProductVariant { ProductVariantId = 1, SKU = "BATT-01", SellingPrice = 100m });
        inventoryRepo.AddInventory(new Inventory { ProductVariantId = 1, QuantityOnHand = 10, QuantityReserved = 0, Version = 1 });

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var dto = new CreateOrderDto
        {
            UserId = 1,
            RecipientName = "Charlie",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductVariantId = 1, Quantity = 2 }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(dto));
        Assert.Equal("Stock conflict detected, please retry.", ex.Message);
        Assert.True(uow.RollbackCalled);
    }

    // =========================================================================
    // 3. P0.7: Order State Transitions & Rollback on Invalid States
    // =========================================================================

    [Theory]
    [InlineData("PendingConfirmation")]
    [InlineData("Confirmed")]
    [InlineData("Processing")]
    [InlineData("Cancelled")]
    public async Task CompleteOrderAsync_OrderStatusNotShipped_RollsBackImmediately(string nonShippedStatus)
    {
        var uow = new AdversarialUnitOfWork();
        var orderRepo = new AdversarialOrderRepository();
        var inventoryRepo = new AdversarialInventoryRepository();
        var variantRepo = new AdversarialVariantRepository();

        var order = new Order
        {
            OrderId = 99,
            OrderStatus = nonShippedStatus,
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 1, Quantity = 3, UnitPrice = 50m }
            }
        };
        orderRepo.AddOrder(order);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CompleteOrderAsync(99);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.Equal(nonShippedStatus, order.OrderStatus); // Status not altered
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Completed")]
    public async Task CancelOrderAsync_OrderAlreadyClosed_RollsBackImmediately(string closedStatus)
    {
        var uow = new AdversarialUnitOfWork();
        var orderRepo = new AdversarialOrderRepository();
        var inventoryRepo = new AdversarialInventoryRepository();
        var variantRepo = new AdversarialVariantRepository();

        var order = new Order
        {
            OrderId = 88,
            OrderStatus = closedStatus,
            Items = new List<OrderItem>
            {
                new() { ProductVariantId = 1, Quantity = 2, UnitPrice = 100m }
            }
        };
        orderRepo.AddOrder(order);

        var service = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var result = await service.CancelOrderAsync(88);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.Equal(closedStatus, order.OrderStatus);
    }

    // =========================================================================
    // 4. P0.7: RepairService Rollback Guarantees
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public async Task AddRepairItemAsync_InvalidQuantity_RollsBackImmediately(int invalidQuantity)
    {
        var uow = new AdversarialUnitOfWork();
        var ticketRepo = new AdversarialRepairTicketRepository();
        var inventoryRepo = new AdversarialInventoryRepository();
        var variantRepo = new AdversarialVariantRepository();

        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(1, 10, invalidQuantity, 50m, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
    }

    [Fact]
    public async Task AddRepairItemAsync_CompletedTicket_RollsBackImmediately()
    {
        var uow = new AdversarialUnitOfWork();
        var ticketRepo = new AdversarialRepairTicketRepository();
        var inventoryRepo = new AdversarialInventoryRepository();
        var variantRepo = new AdversarialVariantRepository();

        var ticket = new RepairTicket
        {
            RepairTicketId = 1,
            TicketStatus = "Completed",
            FinalCost = 200m
        };
        ticketRepo.AddTicket(ticket);

        var service = new RepairService(ticketRepo, inventoryRepo, variantRepo, uow);

        var result = await service.AddRepairItemAsync(1, 10, 1, 50m, 1);

        Assert.False(result);
        Assert.True(uow.RollbackCalled);
        Assert.False(uow.CommitCalled);
        Assert.Empty(ticket.Items);
    }

    // =========================================================================
    // 5. P0.6: Concurrency Token Metadata Assertion
    // =========================================================================

    [Fact]
    public void InventoryModel_VersionIsConfiguredAsConcurrencyToken_AndDefaultsToOne()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=battery_ec;Username=postgres;Password=postgres")
                      .UseSnakeCaseNamingConvention();

        using var dbContext = new BatteryDbContext(optionsBuilder.Options);

        var entityType = dbContext.Model.FindEntityType(typeof(Inventory));
        Assert.NotNull(entityType);

        var versionProp = entityType.FindProperty(nameof(Inventory.Version));
        Assert.NotNull(versionProp);
        Assert.True(versionProp.IsConcurrencyToken, "Inventory.Version must be marked as IsConcurrencyToken");
    }

    // =========================================================================
    // 6. UnitOfWork Lifetime and Disposal Verification
    // =========================================================================

    [Fact]
    public void UnitOfWork_ImplementsIDisposableAndIAsyncDisposable()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(UnitOfWork)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(UnitOfWork)));
    }

    // =========================================================================
    // 7. Live PostgreSQL Empirical Concurrency & Stress Tests
    // =========================================================================

    private const string LiveConnectionString = "Host=localhost;Port=5432;Database=battery_ec;Username=postgres;Password=postgres";

    private BatteryDbContext CreateLiveDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(LiveConnectionString)
                      .UseSnakeCaseNamingConvention();
        return new BatteryDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task LivePostgreSql_SimultaneousUpdatesOnSameInventoryRecord_SecondUpdateTriggersDbUpdateConcurrencyException()
    {
        await using var initContext = CreateLiveDbContext();
        if (!await initContext.Database.CanConnectAsync())
        {
            return; // Skip if database is not reachable in current environment
        }

        var inventory = await initContext.Inventory.FirstOrDefaultAsync(i => i.ProductVariantId == 2);
        Assert.NotNull(inventory);

        int originalReserved = inventory.QuantityReserved;
        int originalVersion = inventory.Version;

        try
        {
            // Two separate DbContext instances simulating two simultaneous update requests
            await using var context1 = CreateLiveDbContext();
            await using var context2 = CreateLiveDbContext();

            var inv1 = await context1.Inventory.FirstAsync(i => i.ProductVariantId == 2);
            var inv2 = await context2.Inventory.FirstAsync(i => i.ProductVariantId == 2);

            // Both contexts start with the matching Version
            Assert.Equal(inv1.Version, inv2.Version);

            // Context 1 completes and commits update first
            inv1.QuantityReserved += 5;
            inv1.Version++;
            inv1.UpdatedAt = DateTime.UtcNow;
            int affected1 = await context1.SaveChangesAsync();
            Assert.Equal(1, affected1);

            // Context 2 now attempts to commit its update using its stale version token
            inv2.QuantityReserved += 3;
            inv2.Version++;
            inv2.UpdatedAt = DateTime.UtcNow;

            // Optimistic concurrency MUST trigger DbUpdateConcurrencyException because WHERE version matches 0 rows
            var concurrencyEx = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context2.SaveChangesAsync());
            Assert.NotNull(concurrencyEx);

            // Verify in isolated context: context 1's atomic change was persisted, context 2 was rejected
            await using var verifyContext = CreateLiveDbContext();
            var verifiedInv = await verifyContext.Inventory.AsNoTracking().FirstAsync(i => i.ProductVariantId == 2);
            Assert.Equal(originalReserved + 5, verifiedInv.QuantityReserved);
            Assert.Equal(originalVersion + 1, verifiedInv.Version);
        }
        finally
        {
            // Restore original state
            await using var cleanupContext = CreateLiveDbContext();
            var cleanupInv = await cleanupContext.Inventory.FirstAsync(i => i.ProductVariantId == 2);
            cleanupInv.QuantityReserved = originalReserved;
            cleanupInv.Version = originalVersion;
            cleanupInv.UpdatedAt = DateTime.UtcNow;
            await cleanupContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task LivePostgreSql_CreateOrderAsync_InsufficientStock_DoesNotAlterQuantityReserved()
    {
        await using var initContext = CreateLiveDbContext();
        if (!await initContext.Database.CanConnectAsync())
        {
            return;
        }

        var inventory = await initContext.Inventory.FirstOrDefaultAsync(i => i.ProductVariantId == 2);
        Assert.NotNull(inventory);

        int originalOnHand = inventory.QuantityOnHand;
        int originalReserved = inventory.QuantityReserved;
        int available = inventory.AvailableStock;

        var uow = new UnitOfWork(initContext);
        var orderRepo = new OrderRepository(initContext);
        var inventoryRepo = new InventoryRepository(initContext);
        var variantRepo = new GenericRepository<ProductVariant>(initContext);
        var orderService = new OrderService(orderRepo, inventoryRepo, variantRepo, uow);

        var dto = new CreateOrderDto
        {
            UserId = 1,
            RecipientName = "Empirical Test Buyer",
            RecipientPhone = "0900000000",
            ShippingAddress = "123 Test St",
            PaymentMethod = "COD",
            Items = new List<CreateOrderItemDto>
            {
                new()
                {
                    ProductVariantId = 2,
                    Quantity = available + 500 // Exceeds available stock
                }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => orderService.CreateOrderAsync(dto));
        Assert.Contains("Insufficient available stock", ex.Message);

        // Verify in fresh DbContext: QuantityReserved and QuantityOnHand in PostgreSQL are completely unchanged
        await using var verifyContext = CreateLiveDbContext();
        var verifiedInv = await verifyContext.Inventory.AsNoTracking().FirstAsync(i => i.ProductVariantId == 2);
        Assert.Equal(originalReserved, verifiedInv.QuantityReserved);
        Assert.Equal(originalOnHand, verifiedInv.QuantityOnHand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public async Task LivePostgreSql_ReserveStockAsync_NonPositiveQuantities_RejectedWithoutStateChange(int invalidQuantity)
    {
        await using var context = CreateLiveDbContext();
        if (!await context.Database.CanConnectAsync())
        {
            return;
        }

        var inventory = await context.Inventory.FirstAsync(i => i.ProductVariantId == 2);
        int originalReserved = inventory.QuantityReserved;
        int originalVersion = inventory.Version;

        var uow = new UnitOfWork(context);
        var inventoryRepo = new InventoryRepository(context);
        var variantRepo = new GenericRepository<ProductVariant>(context);
        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.ReserveStockAsync(2, invalidQuantity);

        Assert.False(result);

        // Verify in fresh DbContext
        await using var verifyContext = CreateLiveDbContext();
        var verifiedInv = await verifyContext.Inventory.AsNoTracking().FirstAsync(i => i.ProductVariantId == 2);
        Assert.Equal(originalReserved, verifiedInv.QuantityReserved);
        Assert.Equal(originalVersion, verifiedInv.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public async Task LivePostgreSql_ReleaseStockAsync_NonPositiveQuantities_RejectedWithoutStateChange(int invalidQuantity)
    {
        await using var context = CreateLiveDbContext();
        if (!await context.Database.CanConnectAsync())
        {
            return;
        }

        var inventory = await context.Inventory.FirstAsync(i => i.ProductVariantId == 2);
        int originalReserved = inventory.QuantityReserved;
        int originalVersion = inventory.Version;

        var uow = new UnitOfWork(context);
        var inventoryRepo = new InventoryRepository(context);
        var variantRepo = new GenericRepository<ProductVariant>(context);
        var service = new InventoryService(inventoryRepo, variantRepo, uow);

        var result = await service.ReleaseStockAsync(2, invalidQuantity);

        Assert.False(result);

        // Verify in fresh DbContext
        await using var verifyContext = CreateLiveDbContext();
        var verifiedInv = await verifyContext.Inventory.AsNoTracking().FirstAsync(i => i.ProductVariantId == 2);
        Assert.Equal(originalReserved, verifiedInv.QuantityReserved);
        Assert.Equal(originalVersion, verifiedInv.Version);
    }

    // =========================================================================
    // TEST DOUBLES FOR ADVERSARIAL TESTING
    // =========================================================================

    private class AdversarialUnitOfWork : IUnitOfWork
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
            if (ThrowOnCommit != null) throw ThrowOnCommit;
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            RollbackCalled = true;
            return Task.CompletedTask;
        }
    }

    private class AdversarialInventoryRepository : IInventoryRepository
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

    private class AdversarialVariantRepository : IGenericRepository<ProductVariant>
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

    private class AdversarialOrderRepository : IOrderRepository
    {
        private readonly Dictionary<long, Order> _orders = new();
        public IReadOnlyList<Order> AllOrders => _orders.Values.ToList();

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

    private class AdversarialRepairTicketRepository : IRepairTicketRepository
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
