using DCF.Api.Charts;
using DCF.Api.Models;
using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

/// <summary>
/// Generic asynchronous chart API: submit a chart request, poll the job until it completes,
/// then render the standardized result. Adding a chart requires no changes here.
/// </summary>
[ApiController]
[Route("api/charts")]
[Authorize]
public class ChartsController(
    IChartEngine engine,
    IChartJobStore jobStore,
    IChartJobQueue jobQueue,
    IUserService userService) : ControllerBase
{
    private string GetSub()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
            ?? throw new InvalidOperationException("No sub claim");
    }

    [HttpGet]
    public IActionResult GetDefinitions()
    {
        return Ok(engine.GetDefinitions());
    }

    [HttpPost("requests")]
    public async Task<IActionResult> Submit([FromBody] ChartRequest request, CancellationToken cancellationToken)
    {
        var user = await userService.GetAsync(GetSub());

        if (user is null)
        {
            return Unauthorized();
        }

        var parameters = new ChartParameters(request.Parameters);
        var validation = await engine.ValidateAsync(request.ChartKey, parameters, user.Id, cancellationToken);

        switch (validation.Status)
        {
            case ChartRequestStatus.UnknownChart:
                return NotFound(new { error = validation.Error });
            case ChartRequestStatus.InvalidParameters:
                return BadRequest(new { error = validation.Error });
            case ChartRequestStatus.Forbidden:
                return Forbid();
        }

        var job = jobStore.Create(user.Id, request.ChartKey, parameters);

        await jobQueue.EnqueueAsync(job.Id, cancellationToken);

        return AcceptedAtAction(nameof(GetJob), new { jobId = job.Id }, ChartJobResponse.From(job));
    }

    [HttpGet("requests/{jobId}")]
    public async Task<IActionResult> GetJob(Guid jobId)
    {
        var user = await userService.GetAsync(GetSub());

        if (user is null)
        {
            return Unauthorized();
        }

        var job = jobStore.Get(jobId);

        // A job belonging to someone else is indistinguishable from one that never existed.
        if (job is null || job.UserId != user.Id)
        {
            return NotFound();
        }

        return Ok(ChartJobResponse.From(job));
    }
}
