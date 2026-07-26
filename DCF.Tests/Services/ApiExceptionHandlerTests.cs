using DCF.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DCF.Tests.Services;

public class ApiExceptionHandlerTests
{
    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Written { get; private set; }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context;

            return ValueTask.FromResult(true);
        }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context;

            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task TryHandleAsync_SetsInternalServerErrorStatusAndGenericTitle()
    {
        var problemDetailsService = new RecordingProblemDetailsService();
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance, problemDetailsService);
        var httpContext = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(httpContext, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError, problemDetailsService.Written?.ProblemDetails.Status);
        Assert.Equal("An unexpected error occurred.", problemDetailsService.Written?.ProblemDetails.Title);
    }

    [Fact]
    public async Task TryHandleAsync_DoesNotLeakExceptionMessageIntoProblemDetails()
    {
        var problemDetailsService = new RecordingProblemDetailsService();
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance, problemDetailsService);
        var httpContext = new DefaultHttpContext();

        await handler.TryHandleAsync(httpContext, new InvalidOperationException("sensitive internal detail"), CancellationToken.None);

        var title = problemDetailsService.Written?.ProblemDetails.Title ?? string.Empty;
        var detail = problemDetailsService.Written?.ProblemDetails.Detail ?? string.Empty;

        Assert.DoesNotContain("sensitive internal detail", title);
        Assert.DoesNotContain("sensitive internal detail", detail);
    }
}
