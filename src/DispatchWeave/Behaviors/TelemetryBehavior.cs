using System.Diagnostics;
using System.Runtime.CompilerServices;
using DispatchWeave.Diagnostics;
using FluentResults;

namespace DispatchWeave.Behaviors;

/// <summary>
/// Pipeline behavior that records OpenTelemetry distributed tracing activities and metrics via <see cref="DispatchWeaveDiagnostics"/>.
/// </summary>
public sealed class TelemetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;
    private static readonly string RequestFullName = typeof(TRequest).FullName ?? RequestName;
    private static readonly string ResponseFullName = typeof(TResponse).FullName ?? typeof(TResponse).Name;
    private static readonly string SpanName = $"DispatchWeave.Send {RequestName}";

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        using var activity = DispatchWeaveDiagnostics.ActivitySource.StartActivity(SpanName, ActivityKind.Internal);
        activity?.SetTag("dispatchweave.request.type", RequestFullName);
        activity?.SetTag("dispatchweave.response.type", ResponseFullName);

        var startTimestamp = Stopwatch.GetTimestamp();
        var tagList = new TagList
        {
            { "request.type", RequestName },
            { "response.type", typeof(TResponse).Name }
        };

        DispatchWeaveDiagnostics.RequestsTotal.Add(1, tagList);

        try
        {
            var response = await next().ConfigureAwait(false);
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (response is IResultBase { IsFailed: true } failedResult)
            {
                var firstError = failedResult.Errors.FirstOrDefault()?.Message ?? "ResultFailed";
                activity?.SetStatus(ActivityStatusCode.Error, firstError);
                activity?.SetTag("dispatchweave.result.status", "failed");
                DispatchWeaveDiagnostics.RequestsFailed.Add(1, tagList);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                activity?.SetTag("dispatchweave.result.status", "ok");
            }

            DispatchWeaveDiagnostics.RequestDurationMs.Record(elapsedMs, tagList);
            return response;
        }
        catch (Exception ex)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);

            DispatchWeaveDiagnostics.RequestsFailed.Add(1, tagList);
            DispatchWeaveDiagnostics.RequestDurationMs.Record(elapsedMs, tagList);
            throw;
        }
    }
}

/// <summary>
/// Stream pipeline behavior that records OpenTelemetry distributed tracing activities and metrics for streaming requests.
/// </summary>
public sealed class StreamTelemetryBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;
    private static readonly string RequestFullName = typeof(TRequest).FullName ?? RequestName;
    private static readonly string ResponseFullName = typeof(TResponse).FullName ?? typeof(TResponse).Name;
    private static readonly string SpanName = $"DispatchWeave.Stream {RequestName}";

    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = DispatchWeaveDiagnostics.ActivitySource.StartActivity(SpanName, ActivityKind.Internal);
        activity?.SetTag("dispatchweave.request.type", RequestFullName);
        activity?.SetTag("dispatchweave.response.type", ResponseFullName);

        var startTimestamp = Stopwatch.GetTimestamp();
        var itemCount = 0L;
        var tagList = new TagList
        {
            { "request.type", RequestName },
            { "response.type", typeof(TResponse).Name }
        };

        DispatchWeaveDiagnostics.RequestsTotal.Add(1, tagList);

        await using var enumerator = next().GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            TResponse current;
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    break;

                current = enumerator.Current;
                itemCount++;
            }
            catch (Exception ex)
            {
                var failedElapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.SetTag("dispatchweave.stream.items", itemCount);
                DispatchWeaveDiagnostics.RequestsFailed.Add(1, tagList);
                DispatchWeaveDiagnostics.RequestDurationMs.Record(failedElapsedMs, tagList);
                throw;
            }

            yield return current;
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        activity?.SetTag("dispatchweave.stream.items", itemCount);
        activity?.SetStatus(ActivityStatusCode.Ok);
        DispatchWeaveDiagnostics.RequestDurationMs.Record(elapsedMs, tagList);
    }
}
