using DCF.Api.Charts;
using DCF.Api.Models;
using DCF.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

/// <summary>
/// Generic asynchronous chart API: submit a chart request, poll the job until it completes,
/// then render the standardized result. Adding a chart requires no changes here.
///
/// Anonymous access is allowed at the transport level - like <c>PublicDciController</c>, a
/// non-league-scoped chart (e.g. the DCI progression chart) needs no account. A league-scoped
/// chart still requires one: <see cref="IChartEngine.ValidateAsync"/> rejects those for a null
/// userId regardless of this controller's own auth state.
/// </summary>
[ApiController]
[Route("api/charts")]
public class ChartsController(
    IChartEngine engine,
    IChartJobStore jobStore,
    IChartJobQueue jobQueue,
    IUserService userService) : ControllerBase
{
    private string? TryGetSub()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    }

    /// <summary>
    /// Resolves the caller, if any. A request that presents no credential at all is anonymous
    /// (null); one that presents a credential naming no known user is still rejected outright -
    /// only a genuinely absent credential gets the anonymous-tier treatment.
    /// </summary>
    private async Task<(UserProfile? User, bool PresentedInvalidCredential)> TryGetUserAsync()
    {
        var sub = TryGetSub();

        if (sub is null)
        {
            return (null, false);
        }

        var user = await userService.GetAsync(sub);

        return (user, user is null);
    }

    [HttpGet]
    public IActionResult GetDefinitions()
    {
        return Ok(engine.GetDefinitions());
    }

    [HttpPost("requests")]
    public async Task<IActionResult> Submit([FromBody] ChartRequest request, CancellationToken cancellationToken)
    {
        var (user, invalidCredential) = await TryGetUserAsync();

        if (invalidCredential)
        {
            return Unauthorized();
        }

        var parameters = new ChartParameters(request.Parameters);
        var validation = await engine.ValidateAsync(request.ChartKey, parameters, user?.Id, cancellationToken);

        switch (validation.Status)
        {
            case ChartRequestStatus.UnknownChart:
                return NotFound(new { error = validation.Error });
            case ChartRequestStatus.InvalidParameters:
                return BadRequest(new { error = validation.Error });
            case ChartRequestStatus.Forbidden:
                return Forbid();
        }

        var job = jobStore.Create(user?.Id, request.ChartKey, parameters);

        await jobQueue.EnqueueAsync(job.Id, cancellationToken);

        return AcceptedAtAction(nameof(GetJob), new { jobId = job.Id }, ChartJobResponse.From(job));
    }

    [HttpGet("requests/{jobId}")]
    public async Task<IActionResult> GetJob(Guid jobId)
    {
        var (user, invalidCredential) = await TryGetUserAsync();

        if (invalidCredential)
        {
            return Unauthorized();
        }

        var job = jobStore.Get(jobId);

        if (job is null)
        {
            return NotFound();
        }

        // An anonymously-submitted job (UserId null - only possible for a non-league-scoped
        // chart) has no owner to check and is exactly as public as the data it charts. A job
        // that does have an owner is only visible to that same signed-in user; a mismatch is
        // indistinguishable from one that never existed.
        if (job.UserId is Guid ownerId && ownerId != user?.Id)
        {
            return NotFound();
        }

        return Ok(ChartJobResponse.From(job));
    }
}
