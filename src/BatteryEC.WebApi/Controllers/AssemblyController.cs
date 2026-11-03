using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Assembly;
using BatteryEC.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssemblyController : ControllerBase
{
    private readonly IAssemblyService _assemblyService;

    public AssemblyController(IAssemblyService assemblyService)
    {
        _assemblyService = assemblyService;
    }

    [HttpPost("execute")]
    public async Task<ActionResult<ApiResponse<AssemblyOrderDto>>> ExecuteAssembly(
        [FromBody] CreateAssemblyOrderDto dto,
        [FromQuery] long userId = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var order = await _assemblyService.ExecuteAssemblyOrderAsync(dto, userId, cancellationToken);
            return CreatedAtAction(nameof(GetAssemblyOrderByCode), new { code = order.AssemblyCode }, ApiResponse<AssemblyOrderDto>.Ok(order, "Assembly order executed and finished goods capitalized."));
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
