using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OptionsTrader.Application.DTOs.Screenshots;
using OptionsTrader.Application.Services;

namespace OptionsTrader.API.Controllers;

[Authorize]

[ApiController]
[Route("api/[controller]")]
public class ScreenshotsController(ScreenshotService screenshotService, TradeService tradeService) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("trade/{tradeId:int}")]
    public async Task<IActionResult> GetByTrade(int tradeId)
    {
        if (await tradeService.GetByIdAsync(tradeId, CurrentUserId) is null)
            return NotFound();

        return Ok(await screenshotService.GetByTradeAsync(tradeId));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateScreenshotDto dto)
    {
        if (await tradeService.GetByIdAsync(dto.TradeId, CurrentUserId) is null)
            return NotFound();

        try
        {
            var result = await screenshotService.SaveUrlAsync(dto);
            return CreatedAtAction(nameof(GetByTrade), new { tradeId = result.TradeId }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}
