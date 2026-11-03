using BatteryEC.Application.DTOs.Repairs;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Services;
using BatteryEC.Domain.Repositories;

namespace BatteryEC.Application.Services;

public class RepairService : IRepairService
{
    private readonly IRepairTicketRepository _repairTicketRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IGenericRepository<ProductVariant> _variantRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RepairService(
        IRepairTicketRepository repairTicketRepository,
        IInventoryRepository inventoryRepository,
        IGenericRepository<ProductVariant> variantRepository,
        IUnitOfWork unitOfWork)
    {
        _repairTicketRepository = repairTicketRepository;
        _inventoryRepository = inventoryRepository;
        _variantRepository = variantRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<RepairTicketDto> CreateRepairTicketAsync(CreateRepairTicketDto dto, long createdByUserId, CancellationToken cancellationToken = default)
    {
        var ticket = new RepairTicket
        {
            TicketCode = $"REP-{DateTime.UtcNow:yyyyMMddHHmmss}-{createdByUserId}",
            CustomerId = dto.CustomerId,
            CustomerName = dto.CustomerName,
            CustomerPhone = dto.CustomerPhone,
            DeviceModel = dto.DeviceModel,
            SerialOrPinCode = dto.SerialOrPinCode,
            IssueDescription = dto.IssueDescription,
            EstimatedCost = dto.EstimatedCost,
            WarrantyMonths = dto.WarrantyMonths,
            TicketStatus = "Received",
            PaymentStatus = "Unpaid",
            CreatedByUserId = createdByUserId,
            ReceivedAt = DateTime.UtcNow
        };

        await _repairTicketRepository.AddAsync(ticket, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(ticket);
    }

    public async Task<RepairTicketDto?> GetRepairTicketByCodeAsync(string ticketCode, CancellationToken cancellationToken = default)
    {
        var ticket = await _repairTicketRepository.GetByCodeAsync(ticketCode, cancellationToken);
        if (ticket == null) return null;

        return MapToDto(ticket);
    }

    public async Task<bool> AddRepairItemAsync(long ticketId, long productVariantId, int quantity, decimal unitPrice, long userId, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var ticket = await _repairTicketRepository.GetWithItemsAsync(ticketId, cancellationToken);
            if (ticket == null || ticket.TicketStatus == "Completed" || ticket.TicketStatus == "Delivered")
            {
                return false;
            }

            var variant = await _variantRepository.GetByIdAsync(productVariantId, cancellationToken);
            if (variant == null) return false;

            decimal lineTotal = quantity * unitPrice;
            ticket.Items.Add(new RepairTicketItem
            {
                RepairTicketId = ticketId,
                ProductVariantId = productVariantId,
                Quantity = quantity,
                UnitPrice = unitPrice,
                LineTotal = lineTotal
            });

            // Update ticket final/estimated cost
            ticket.FinalCost += lineTotal;

            // Deduct spare part from inventory
            var inventory = await _inventoryRepository.GetByVariantIdAsync(productVariantId, cancellationToken);
            if (inventory != null)
            {
                inventory.QuantityOnHand -= quantity;
                inventory.UpdatedAt = DateTime.UtcNow;
                _inventoryRepository.Update(inventory);
            }

            await _inventoryRepository.AddTransactionAsync(new BatteryEC.Domain.Entities.Inventory.InventoryTransaction
            {
                ProductVariantId = productVariantId,
                TransactionType = "RepairConsumption",
                QuantityChange = -quantity,
                ReferenceType = "RepairTicket",
                ReferenceId = ticketId,
                UnitCost = variant.CostPrice ?? 0,
                CreatedByUserId = userId,
                Note = $"Parts used in repair ticket {ticket.TicketCode}",
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);

            _repairTicketRepository.Update(ticket);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return true;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> UpdateStatusAsync(long ticketId, string status, string? solutionNote, decimal? finalCost, CancellationToken cancellationToken = default)
    {
        var ticket = await _repairTicketRepository.GetWithItemsAsync(ticketId, cancellationToken);
        if (ticket == null) return false;

        ticket.TicketStatus = status;
        if (!string.IsNullOrWhiteSpace(solutionNote))
        {
            ticket.SolutionNote = solutionNote;
        }

        if (finalCost.HasValue)
        {
            ticket.FinalCost = finalCost.Value;
        }

        if (status == "Completed")
        {
            ticket.CompletedAt = DateTime.UtcNow;
        }
        else if (status == "Delivered")
        {
            ticket.DeliveredAt = DateTime.UtcNow;
        }

        _repairTicketRepository.Update(ticket);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static RepairTicketDto MapToDto(RepairTicket ticket)
    {
        return new RepairTicketDto
        {
            RepairTicketId = ticket.RepairTicketId,
            TicketCode = ticket.TicketCode,
            CustomerId = ticket.CustomerId,
            CustomerName = ticket.CustomerName,
            CustomerPhone = ticket.CustomerPhone,
            DeviceModel = ticket.DeviceModel,
            SerialOrPinCode = ticket.SerialOrPinCode,
            IssueDescription = ticket.IssueDescription,
            SolutionNote = ticket.SolutionNote,
            EstimatedCost = ticket.EstimatedCost,
            FinalCost = ticket.FinalCost,
            PaidAmount = ticket.PaidAmount,
            TicketStatus = ticket.TicketStatus,
            WarrantyMonths = ticket.WarrantyMonths,
            ReceivedAt = ticket.ReceivedAt,
            CompletedAt = ticket.CompletedAt,
            DeliveredAt = ticket.DeliveredAt,
            CreatedByUserId = ticket.CreatedByUserId,
            TechnicianId = ticket.TechnicianUserId,
            Items = ticket.Items.Select(i => new RepairTicketItemDto
            {
                RepairTicketItemId = i.RepairTicketItemId,
                ProductVariantId = i.ProductVariantId,
                ComponentName = i.ProductVariant?.VariantName ?? string.Empty,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                LineTotal = i.LineTotal
            }).ToList()
        };
    }
}
