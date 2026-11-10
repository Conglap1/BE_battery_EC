using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Repairs;
using BatteryEC.Application.Services;
using BatteryEC.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[Authorize(Roles = $"{RoleType.Technician},{RoleType.Admin}")]
[ApiController]
[Route("api/[controller]")]
public class RepairsController : ControllerBase
{
    private readonly IRepairService _repairService;
    private readonly ICurrentUserService _currentUserService;

    public RepairsController(IRepairService repairService, ICurrentUserService currentUserService)
    {
        _repairService = repairService;
        _currentUserService = currentUserService;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<RepairTicketDto>>> CreateRepairTicket(
        [FromBody] CreateRepairTicketDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Security P0.4: Actor identity extracted from authenticated JWT context
            var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User is not authenticated");
            var ticket = await _repairService.CreateRepairTicketAsync(dto, userId, cancellationToken);
            return CreatedAtAction(nameof(GetRepairTicketByCode), new { code = ticket.TicketCode }, ApiResponse<RepairTicketDto>.Ok(ticket, "Repair intake ticket created."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<RepairTicketDto>.Fail(ex.Message));
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

    [HttpPost("{id:long}/items")]
    public async Task<ActionResult<ApiResponse<bool>>> AddRepairItem(
        long id,
        [FromQuery] long variantId,
        [FromQuery] int quantity = 1,
        [FromQuery] decimal unitPrice = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Security P0.4: Actor identity extracted from authenticated JWT context
            var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User is not authenticated");
            var success = await _repairService.AddRepairItemAsync(id, variantId, quantity, unitPrice, userId, cancellationToken);
            if (!success)
            {
                return BadRequest(ApiResponse<bool>.Fail($"Unable to add component #{variantId} to repair ticket #{id}."));
            }

            return Ok(ApiResponse<bool>.Ok(true, "Spare part consumed from inventory and added to repair ticket."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    [HttpPut("{id:long}/status")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateStatus(
        long id,
        [FromQuery] string status,
        [FromQuery] string? solutionNote,
        [FromQuery] decimal? finalCost,
        [FromQuery] int? warrantyMonths,
        CancellationToken cancellationToken)
    {
        var success = await _repairService.UpdateStatusAsync(id, status, solutionNote, finalCost, warrantyMonths, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail($"Unable to update status for repair ticket #{id}."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Repair ticket status updated successfully."));
    }
}
