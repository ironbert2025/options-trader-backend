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
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DateOnly? date, [FromQuery] string? month)
    {
        if (date.HasValue)
            return Ok(await tradeService.GetByDateAsync(date.Value));

        if (month is not null)
        {
            if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var parsed))
                return BadRequest("month must be in yyyy-MM format (e.g. 2026-06)");
            return Ok(await tradeService.GetByMonthAsync(parsed.Year, parsed.Month));
        }

        return Ok(await tradeService.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var trade = await tradeService.GetByIdAsync(id);
        return trade is null ? NotFound() : Ok(trade);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTradeDto dto)
    {
        var trade = await tradeService.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = trade.Id }, trade);
    }

    [HttpPatch("{id:int}/close")]
    public async Task<IActionResult> Close(int id, CloseTradeDto dto)
    {
        try
        {
            var trade = await tradeService.CloseAsync(id, dto);
            return Ok(trade);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
