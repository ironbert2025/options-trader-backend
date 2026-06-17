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
    public async Task<IActionResult> GetAll() =>
        Ok(await tradeService.GetAllAsync());

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
