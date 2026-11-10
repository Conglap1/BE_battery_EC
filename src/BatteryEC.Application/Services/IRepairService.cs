using BatteryEC.Application.DTOs.Repairs;

namespace BatteryEC.Application.Services;

public interface IRepairService
{
    Task<RepairTicketDto> CreateRepairTicketAsync(CreateRepairTicketDto dto, long createdByUserId, CancellationToken cancellationToken = default);
    Task<RepairTicketDto?> GetRepairTicketByCodeAsync(string ticketCode, CancellationToken cancellationToken = default);
    Task<bool> AddRepairItemAsync(long ticketId, long productVariantId, int quantity, decimal unitPrice, long userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateStatusAsync(long ticketId, string status, string? solutionNote, decimal? finalCost, int? warrantyMonths = null, CancellationToken cancellationToken = default);
}
