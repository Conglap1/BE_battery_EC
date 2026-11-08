using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Assembly;
using BatteryEC.Application.Services;
using BatteryEC.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[Authorize(Roles = $"{RoleType.Technician},{RoleType.Admin}")]
[ApiController]
[Route("api/[controller]")]
public class AssemblyController : ControllerBase
{
    private readonly IAssemblyService _assemblyService;
    private readonly ICurrentUserService _currentUserService;

    public AssemblyController(IAssemblyService assemblyService, ICurrentUserService currentUserService)
    {
        _assemblyService = assemblyService;
        _currentUserService = currentUserService;
    }

    [HttpPost("execute")]
    public async Task<ActionResult<ApiResponse<AssemblyOrderDto>>> ExecuteAssembly(
        [FromBody] CreateAssemblyOrderDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Security P0.4: Actor identity extracted from authenticated JWT context
            var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User is not authenticated");
            var order = await _assemblyService.ExecuteAssemblyOrderAsync(dto, userId, cancellationToken);
            return CreatedAtAction(nameof(GetAssemblyOrderByCode), new { code = order.AssemblyCode }, ApiResponse<AssemblyOrderDto>.Ok(order, "Assembly order executed and finished goods capitalized."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<AssemblyOrderDto>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<AssemblyOrderDto>.Fail(ex.Message));
        }
    }

    [HttpGet("{code}")]
    public async Task<ActionResult<ApiResponse<AssemblyOrderDto>>> GetAssemblyOrderByCode(string code, CancellationToken cancellationToken)
    {
        var order = await _assemblyService.GetAssemblyOrderByCodeAsync(code, cancellationToken);
        if (order == null)
        {
            return NotFound(ApiResponse<AssemblyOrderDto>.Fail($"Assembly order '{code}' not found."));
        }

        return Ok(ApiResponse<AssemblyOrderDto>.Ok(order));
    }
}
