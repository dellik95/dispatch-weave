using System.Diagnostics;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace DispatchWeave.Behaviors;

/// <summary>
/// Pipeline behavior that logs request execution start, completion duration, and errors using <see cref="ILogger{TCategoryName}"/>.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Dispatching request {RequestName}", RequestName);

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var response = await next().ConfigureAwait(false);
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (response is IResultBase { IsFailed: true } failedResult)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(
                        "Request {RequestName} completed with {ErrorCount} error(s) in {ElapsedMs:F2} ms",
                        RequestName,
                        failedResult.Errors.Count,
                        elapsedMs);
                }
            }
            else if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Request {RequestName} completed in {ElapsedMs:F2} ms",
                    RequestName,
                    elapsedMs);
            }

            return response;
        }
        catch (Exception ex)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            logger.LogError(
                ex,
                "Request {RequestName} failed after {ElapsedMs:F2} ms",
                RequestName,
                elapsedMs);
            throw;
        }
    }
}
