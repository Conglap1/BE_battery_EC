using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Repairs;
using BatteryEC.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RepairsController : ControllerBase
{
    private readonly IRepairService _repairService;

    public RepairsController(IRepairService repairService)
    {
        _repairService = repairService;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<RepairTicketDto>>> CreateRepairTicket(
        [FromBody] CreateRepairTicketDto dto,
        [FromQuery] long userId = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ticket = await _repairService.CreateRepairTicketAsync(dto, userId, cancellationToken);
            return CreatedAtAction(nameof(GetRepairTicketByCode), new { code = ticket.TicketCode }, ApiResponse<RepairTicketDto>.Ok(ticket, "Repair intake ticket created."));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<RepairTicketDto>.Fail(ex.Message));
        }
    }

    [HttpGet("{code}")]
    public async Task<ActionResult<ApiResponse<RepairTicketDto>>> GetRepairTicketByCode(string code, CancellationToken cancellationToken)
    {
        var ticket = await _repairService.GetRepairTicketByCodeAsync(code, cancellationToken);
        if (ticket == null)
        {
            return NotFound(ApiResponse<RepairTicketDto>.Fail($"Repair ticket '{code}' not found."));
        }

        return Ok(ApiResponse<RepairTicketDto>.Ok(ticket));
    }

    [HttpPut("{id:long}/status")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateStatus(
        long id,
        [FromQuery] string status,
        [FromQuery] string? solutionNote,
        [FromQuery] decimal? finalCost,
        CancellationToken cancellationToken)
    {
        var success = await _repairService.UpdateStatusAsync(id, status, solutionNote, finalCost, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail($"Unable to update status for repair ticket #{id}."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Repair ticket status updated successfully."));
    }
}
