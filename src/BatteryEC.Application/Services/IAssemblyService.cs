using BatteryEC.Application.DTOs.Assembly;

namespace BatteryEC.Application.Services;

public interface IAssemblyService
{
    Task<AssemblyOrderDto> ExecuteAssemblyOrderAsync(CreateAssemblyOrderDto dto, long userId, CancellationToken cancellationToken = default);
    Task<AssemblyOrderDto?> GetAssemblyOrderByCodeAsync(string assemblyCode, CancellationToken cancellationToken = default);
}
