using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OptionsTrader.Application.DTOs.Trades;
using OptionsTrader.Application.Services;

namespace OptionsTrader.API.Controllers;

[Authorize]

[ApiController]
[Route("api/[controller]")]
public class TradesController(TradeService tradeService) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DateOnly? date, [FromQuery] string? month)
    {
        if (date.HasValue)
            return Ok(await tradeService.GetByDateAsync(date.Value, CurrentUserId));

        if (month is not null)
        {
            if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var parsed))
                return BadRequest("month must be in yyyy-MM format (e.g. 2026-06)");
            return Ok(await tradeService.GetByMonthAsync(parsed.Year, parsed.Month, CurrentUserId));
        }

        return Ok(await tradeService.GetAllAsync(CurrentUserId));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var trade = await tradeService.GetByIdAsync(id, CurrentUserId);
        return trade is null ? NotFound() : Ok(trade);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTradeDto dto)
    {
        var trade = await tradeService.CreateAsync(dto, CurrentUserId);
        return CreatedAtAction(nameof(GetById), new { id = trade.Id }, trade);
    }

    [HttpPatch("{id:int}/close")]
    public async Task<IActionResult> Close(int id, CloseTradeDto dto)
    {
        try
        {
            var trade = await tradeService.CloseAsync(id, dto, CurrentUserId);
            return Ok(trade);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
