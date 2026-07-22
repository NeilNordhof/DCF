using DCF.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/dci")]
public class PublicDciController(IDciPublicService dciPublicService) : ControllerBase
{
    [HttpGet("seasons/current")]
    public async Task<IActionResult> GetCurrentSeason()
    {
        var season = await dciPublicService.GetCurrentSeasonAsync();

        return season is null ? NotFound() : Ok(season);
    }

    [HttpGet("seasons/{seasonId}/standings")]
    public async Task<IActionResult> GetStandings(Guid seasonId)
    {
        return Ok(await dciPublicService.GetStandingsAsync(seasonId));
    }
}
