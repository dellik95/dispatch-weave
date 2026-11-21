using System.Diagnostics;
using System.Runtime.CompilerServices;
using DispatchWeave.Behaviors;
using DispatchWeave.Diagnostics;
using DispatchWeave.Registration;
using DispatchWeave.Results;
using FluentResults;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DispatchWeave.Tests;

public class FeaturesAndBehaviorsTests
{
    // --- Void / Unit Request ---
    public sealed record PingCommand(List<string> Log) : IRequest;

    public sealed class PingCommandHandler : IRequestHandler<PingCommand>
    {
        public Task Handle(PingCommand request, CancellationToken cancellationToken = default)
        {
            request.Log.Add("handled");
            return Task.CompletedTask;
        }
    }

    // --- Streaming Request ---
    public sealed record CountStream(int Count) : IStreamRequest<int>;

    public sealed class CountStreamHandler : IStreamRequestHandler<CountStream, int>
    {
        public async IAsyncEnumerable<int> Handle(
            CountStream request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 1; i <= request.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return i;
            }
        }
    }

    [PipelineOrder(10)]
    public sealed class MultiplyStreamBehavior : IStreamPipelineBehavior<CountStream, int>
    {
        public async IAsyncEnumerable<int> Handle(
            CountStream request,
            StreamHandlerDelegate<int> next,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in next().WithCancellation(cancellationToken))
            {
                yield return item * 10;
            }
        }
    }

    [PipelineOrder(-10)]
    public sealed class OffsetStreamBehavior : IStreamPipelineBehavior<CountStream, int>
    {
        public async IAsyncEnumerable<int> Handle(
            CountStream request,
            StreamHandlerDelegate<int> next,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in next().WithCancellation(cancellationToken))
            {
                yield return item + 1;
            }
        }
    }

    // --- Cacheable / Retryable / Timeout Requests ---
    public sealed record CachedQuery(string Key, TimeSpan? Ttl = null) : IQuery<int>, ICacheableRequest
    {
        public string CacheKey => Key;
        public TimeSpan? TimeToLive => Ttl;
    }

    public sealed class CachedQueryHandler : IQueryHandler<CachedQuery, int>
    {
        public int ExecutionCount { get; private set; }
        public bool ShouldFail { get; set; }

        public Task<Result<int>> Handle(CachedQuery request, CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(ShouldFail ? Result.Fail<int>("failed") : Result.Ok(ExecutionCount));
        }
    }

    public sealed record SlowRequest(TimeSpan Delay, TimeSpan Timeout) : IRequest<string>, ITimeoutRequest;

    public sealed class SlowRequestHandler : IRequestHandler<SlowRequest, string>
    {
        public async Task<string> Handle(SlowRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(request.Delay, cancellationToken);
            return "completed";
        }
    }

    public sealed record FlakyRequest(int FailTimes, int MaxRetries) : IRequest<string>, IRetryableRequest;

    public sealed class FlakyRequestHandler : IRequestHandler<FlakyRequest, string>
    {
        public int Attempts { get; private set; }

        public Task<string> Handle(FlakyRequest request, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts <= request.FailTimes)
                throw new InvalidOperationException($"Transient failure #{Attempts}");

            return Task.FromResult($"Success on attempt {Attempts}");
        }
    }

    // --- Pre/Post Processors & Exception Handlers ---
    public sealed record ProcessedRequest(string Value, bool ThrowError = false) : IRequest<string>;

    public sealed class ProcessedRequestHandler : IRequestHandler<ProcessedRequest, string>
    {
        public Task<string> Handle(ProcessedRequest request, CancellationToken cancellationToken = default)
        {
            if (request.ThrowError)
                throw new ArgumentException("Invalid argument value");

            return Task.FromResult(request.Value.ToUpperInvariant());
        }
    }

    public sealed class SamplePreProcessor(List<string> steps) : IRequestPreProcessor<ProcessedRequest>
    {
        public Task Process(ProcessedRequest request, CancellationToken cancellationToken = default)
        {
            steps.Add($"pre:{request.Value}");
            return Task.CompletedTask;
        }
    }

    public sealed class SamplePostProcessor(List<string> steps) : IRequestPostProcessor<ProcessedRequest, string>
    {
        public Task Process(ProcessedRequest request, string response, CancellationToken cancellationToken = default)
        {
            steps.Add($"post:{response}");
            return Task.CompletedTask;
        }
    }

    public sealed class SampleExceptionAction(List<string> steps) : IRequestExceptionAction<ProcessedRequest, Exception>
    {
        public Task Execute(ProcessedRequest request, Exception exception, CancellationToken cancellationToken = default)
        {
            steps.Add($"action:{exception.GetType().Name}");
            return Task.CompletedTask;
        }
    }

    public sealed class ArgumentExceptionFallbackHandler(List<string> steps)
        : IRequestExceptionHandler<ProcessedRequest, string, ArgumentException>
    {
        public Task Handle(
            ProcessedRequest request,
            ArgumentException exception,
            RequestExceptionHandlerState<string> state,
            CancellationToken cancellationToken = default)
        {
            steps.Add("handled:ArgumentException");
            state.SetHandled("FALLBACK");
            return Task.CompletedTask;
        }
    }

    // --- Polymorphic Notifications ---
    public interface IDomainNotification : INotification
    {
        string Name { get; }
    }

    public sealed class NotificationTestLog : List<string>;

    public abstract record BaseDomainEvent(string Name) : IDomainNotification;
    public sealed record UserRegisteredEvent(string Name) : BaseDomainEvent(Name);
    public readonly record struct StructNotification(string Code) : INotification;

    public sealed class ConcreteEventHandler(NotificationTestLog log) : INotificationHandler<UserRegisteredEvent>
    {
        public Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken = default)
        {
            log.Add($"concrete:{notification.Name}");
            return Task.CompletedTask;
        }
    }

    public sealed class BaseEventHandler(NotificationTestLog log) : INotificationHandler<BaseDomainEvent>
    {
        public Task Handle(BaseDomainEvent notification, CancellationToken cancellationToken = default)
        {
            log.Add($"base:{notification.Name}");
            return Task.CompletedTask;
        }
    }

    public sealed class InterfaceEventHandler(NotificationTestLog log) : INotificationHandler<IDomainNotification>
    {
        public Task Handle(IDomainNotification notification, CancellationToken cancellationToken = default)
        {
            log.Add($"iface:{notification.Name}");
            return Task.CompletedTask;
        }
    }

    public sealed class RootNotificationHandler<TTag>(NotificationTestLog log) : INotificationHandler<INotification>
    {
        public Task Handle(INotification notification, CancellationToken cancellationToken = default)
        {
            log.Add($"root:{notification.GetType().Name}");
            return Task.CompletedTask;
        }
    }

    // --- Pipeline Ordering ---
    private sealed class OrderedBehavior(string label, int order, List<string> trace)
        : IPipelineBehavior<PingCommand, Unit>, IOrderedBehavior
    {
        public int Order => order;

        public async Task<Unit> Handle(
            PingCommand request,
            RequestHandlerDelegate<Unit> next,
            CancellationToken cancellationToken = default)
        {
            trace.Add($"enter:{label}");
            var result = await next();
            trace.Add($"exit:{label}");
            return result;
        }
    }

    [Fact]
    public async Task Void_request_returns_Unit_and_orders_pipeline_behaviors_by_priority()
    {
        var trace = new List<string>();
        var handlers = new HandlerRegistry();
        handlers.Register(new PingCommandHandler());

        var pipelines = new PipelineRegistry();
        pipelines.AddBehavior(new OrderedBehavior("second", 20, trace));
        pipelines.AddBehavior(new OrderedBehavior("first", -10, trace));
        pipelines.AddBehavior(new OrderedBehavior("middle", 5, trace));

        var dispatcher = new Dispatcher(new RegistryRoutes(handlers, pipelines, new NotificationRegistry()));
        var result = await dispatcher.Send(new PingCommand(trace));

        Assert.Equal(Unit.Value, result);
        Assert.Equal(
            ["enter:first", "enter:middle", "enter:second", "handled", "exit:second", "exit:middle", "exit:first"],
            trace);
    }

    [Fact]
    public async Task Streaming_request_executes_ordered_stream_behaviors()
    {
        var handlers = new HandlerRegistry();
        handlers.RegisterStream(new CountStreamHandler());

        var pipelines = new PipelineRegistry();
        // Multiply has order 10 (inner), Offset has order -10 (outer: wraps Multiply)
        // Handler yields 1, 2, 3 -> Multiply yields 10, 20, 30 -> Offset yields 11, 21, 31
        pipelines.AddStreamBehavior(new MultiplyStreamBehavior());
        pipelines.AddStreamBehavior(new OffsetStreamBehavior());

        var dispatcher = new Dispatcher(new RegistryRoutes(handlers, pipelines, new NotificationRegistry()));
        var items = new List<int>();
        await foreach (var item in dispatcher.CreateStream(new CountStream(3)))
        {
            items.Add(item);
        }

        Assert.Equal([11, 21, 31], items);
    }

    [Fact]
    public async Task Pre_and_post_processors_and_exception_handlers_execute_in_sequence()
    {
        var services = new ServiceCollection();
        var steps = new List<string>();
        services.AddSingleton(steps);
        services.AddDispatchWeave(b => b
            .RegisterHandlers(typeof(ProcessedRequest).Assembly)
            .WithExceptionHandling()
            .WithPreAndPostProcessors());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var ok = await sender.Send(new ProcessedRequest("hello"));
        Assert.Equal("HELLO", ok);
        Assert.Equal(["pre:hello", "post:HELLO"], steps);

        steps.Clear();
        var fallback = await sender.Send(new ProcessedRequest("bad", ThrowError: true));
        Assert.Equal("FALLBACK", fallback);
        Assert.Equal(["pre:bad", "action:ArgumentException", "handled:ArgumentException"], steps);
    }

    [Fact]
    public async Task Caching_behavior_caches_successful_results_and_skips_failed_results()
    {
        var fakeClock = new ManualTimeProvider();
        var cache = new InMemoryDispatchCache(fakeClock);
        var queryHandler = new CachedQueryHandler();

        var handlers = new HandlerRegistry();
        handlers.Register<CachedQuery, Result<int>>(() => queryHandler);

        var pipelines = new PipelineRegistry();
        pipelines.AddBehavior(new CachingBehavior<CachedQuery, Result<int>>(cache));

        var dispatcher = new Dispatcher(new RegistryRoutes(handlers, pipelines, new NotificationRegistry()));

        // Failed result should not be cached
        queryHandler.ShouldFail = true;
        var failed1 = await dispatcher.Send(new CachedQuery("k1", TimeSpan.FromMinutes(1)));
        Assert.True(failed1.IsFailed);
        Assert.Equal(1, queryHandler.ExecutionCount);

        // Next call succeeds and is cached
        queryHandler.ShouldFail = false;
        var ok1 = await dispatcher.Send(new CachedQuery("k1", TimeSpan.FromMinutes(1)));
        var ok2 = await dispatcher.Send(new CachedQuery("k1", TimeSpan.FromMinutes(1)));
        Assert.Equal(2, ok1.Value);
        Assert.Equal(2, ok2.Value);
        Assert.Equal(2, queryHandler.ExecutionCount);

        // Advance clock past TTL -> cache entry expires
        fakeClock.Advance(TimeSpan.FromMinutes(2));
        var ok3 = await dispatcher.Send(new CachedQuery("k1", TimeSpan.FromMinutes(1)));
        Assert.Equal(3, ok3.Value);
        Assert.Equal(3, queryHandler.ExecutionCount);
    }

    [Fact]
    public async Task Timeout_behavior_throws_TimeoutException_when_request_exceeds_deadline()
    {
        var handlers = new HandlerRegistry();
        handlers.Register(new SlowRequestHandler());

        var pipelines = new PipelineRegistry();
        pipelines.AddBehavior(new TimeoutBehavior<SlowRequest, string>());

        var dispatcher = new Dispatcher(new RegistryRoutes(handlers, pipelines, new NotificationRegistry()));

        var fast = await dispatcher.Send(new SlowRequest(TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(2)));
        Assert.Equal("completed", fast);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            dispatcher.Send(new SlowRequest(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(30))));
    }

    [Fact]
    public async Task Retry_behavior_retries_transient_failures_until_success()
    {
        var flakyHandler = new FlakyRequestHandler();
        var handlers = new HandlerRegistry();
        handlers.Register<FlakyRequest, string>(() => flakyHandler);

        var pipelines = new PipelineRegistry();
        pipelines.AddBehavior(new RetryBehavior<FlakyRequest, string>());

        var dispatcher = new Dispatcher(new RegistryRoutes(handlers, pipelines, new NotificationRegistry()));

        var response = await dispatcher.Send(new FlakyRequest(FailTimes: 2, MaxRetries: 3));
        Assert.Equal("Success on attempt 3", response);
        Assert.Equal(3, flakyHandler.Attempts);
    }

    [Fact]
    public async Task Polymorphic_notification_dispatch_invokes_concrete_base_interface_and_root_handlers()
    {
        var log = new NotificationTestLog();
        var notifications = new NotificationRegistry();
        notifications.AddHandler(new ConcreteEventHandler(log));
        notifications.AddHandler(new BaseEventHandler(log));
        notifications.AddHandler(new InterfaceEventHandler(log));
        notifications.AddHandler(new RootNotificationHandler<object>(log));

        var dispatcher = new Dispatcher(new RegistryRoutes(new HandlerRegistry(), new PipelineRegistry(), notifications));

        await dispatcher.Publish((object)new UserRegisteredEvent("Alice"));
        Assert.Equal(
            ["concrete:Alice", "base:Alice", "iface:Alice", "root:UserRegisteredEvent"],
            log);

        log.Clear();
        await dispatcher.Publish(new StructNotification("S1"));
        Assert.Equal(["root:StructNotification"], log);
    }

    [Fact]
    public async Task Notification_publish_strategies_handle_exceptions_and_concurrency_as_configured()
    {
        var called = new List<string>();
        var notifications = new NotificationRegistry();
        notifications.AddHandler<UserRegisteredEvent>(new DelegatingNotificationHandler<UserRegisteredEvent>((_, _) =>
        {
            called.Add("h1");
            throw new InvalidOperationException("err1");
        }));
        notifications.AddHandler<UserRegisteredEvent>(new DelegatingNotificationHandler<UserRegisteredEvent>((_, _) =>
        {
            called.Add("h2");
            throw new InvalidOperationException("err2");
        }));

        // 1. ContinueAndAggregateExceptions runs all handlers and throws AggregateException
        var continueOptions = new DispatchWeaveOptions
        {
            NotificationPublishStrategy = NotificationPublishStrategy.ContinueAndAggregateExceptions
        };
        var continueDispatcher = new Dispatcher(
            new RegistryRoutes(new HandlerRegistry(), new PipelineRegistry(), notifications, continueOptions));

        var agg = await Assert.ThrowsAsync<AggregateException>(() =>
            continueDispatcher.Publish(new UserRegisteredEvent("Bob")));
        Assert.Equal(2, agg.InnerExceptions.Count);
        Assert.Equal(["h1", "h2"], called);

        // 2. ParallelWhenAll runs concurrently and aggregates multiple exceptions
        var parallelOptions = new DispatchWeaveOptions
        {
            NotificationPublishStrategy = NotificationPublishStrategy.ParallelWhenAll
        };
        var parallelDispatcher = new Dispatcher(
            new RegistryRoutes(new HandlerRegistry(), new PipelineRegistry(), notifications, parallelOptions));

        var parallelAgg = await Assert.ThrowsAsync<AggregateException>(() =>
            parallelDispatcher.Publish(new UserRegisteredEvent("Bob")));
        Assert.Equal(2, parallelAgg.InnerExceptions.Count);

        // 3. FireAndForget captures background exceptions via callback
        var tcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fireAndForgetOptions = new DispatchWeaveOptions
        {
            NotificationPublishStrategy = NotificationPublishStrategy.FireAndForget,
            FireAndForgetExceptionHandler = (ex, _) => tcs.TrySetResult(ex)
        };
        var ffDispatcher = new Dispatcher(
            new RegistryRoutes(new HandlerRegistry(), new PipelineRegistry(), notifications, fireAndForgetOptions));

        await ffDispatcher.Publish(new UserRegisteredEvent("Async"));
        var caught = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<InvalidOperationException>(caught);
    }

    [Fact]
    public async Task Telemetry_and_logging_behaviors_record_activities_and_logs()
    {
        var recordedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == DispatchWeaveDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => recordedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new List<string>());
        services.AddDispatchWeave(b => b
            .RegisterHandlers(typeof(PingCommand).Assembly)
            .WithLogging()
            .WithTelemetry());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher.Send(new PingCommand([]));
        var items = new List<int>();
        await foreach (var i in dispatcher.CreateStream(new CountStream(2)))
            items.Add(i);

        Assert.Equal([1, 2], items);
        Assert.Contains(recordedActivities, a => a.OperationName == "DispatchWeave.Send PingCommand" && a.Status == ActivityStatusCode.Ok);
        Assert.Contains(recordedActivities, a => a.OperationName == "DispatchWeave.Stream CountStream" && a.Status == ActivityStatusCode.Ok);
    }

    public sealed class ProcessedRequestValidator : AbstractValidator<ProcessedRequest>
    {
        public ProcessedRequestValidator() =>
            RuleFor(x => x.Value).NotEmpty().WithMessage("Value cannot be empty");
    }

    public sealed class BaseQueryBehavior(List<string> log) : IPipelineBehavior<IQuery<string>, Result<string>>
    {
        public async Task<Result<string>> Handle(
            IQuery<string> request,
            RequestHandlerDelegate<Result<string>> next,
            CancellationToken cancellationToken = default)
        {
            log.Add("base-query-behavior");
            return await next();
        }
    }

    [Fact]
    public async Task ThrowingValidationBehavior_throws_ValidationException_on_non_result_requests()
    {
        var services = new ServiceCollection();
        services.AddScoped<IValidator<ProcessedRequest>, ProcessedRequestValidator>();
        services.AddDispatchWeave(b => b
            .RegisterHandlersFromAssemblyContaining<ProcessedRequest>()
            .WithThrowingValidation());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        Assert.Equal("OK", await sender.Send(new ProcessedRequest("ok")));
        await Assert.ThrowsAsync<ValidationException>(() => sender.Send(new ProcessedRequest("")));
    }

    [Fact]
    public async Task Inherited_base_request_behaviors_execute_in_DI_and_explicit_registries()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddDispatchWeave(b => b
            .RegisterHandlersFromAssemblyContaining<DiContainersTests.GreetQuery>()
            .AddBehavior<BaseQueryBehavior>());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var res = await sender.Send(new DiContainersTests.GreetQuery("World"));
        Assert.Equal("Hello, World!", res.Value);
        Assert.Equal(["base-query-behavior"], log);
    }

    [Fact]
    public async Task Null_factory_and_invalid_notification_throw_structured_HandlerResolutionException()
    {
        var handlers = new HandlerRegistry();
        handlers.Register<ProcessedRequest, string>(() => null!);
        var dispatcher = new Dispatcher(new RegistryRoutes(handlers, new PipelineRegistry(), new NotificationRegistry()));

        var nullEx = await Assert.ThrowsAsync<HandlerResolutionException>(() =>
            dispatcher.Send(new ProcessedRequest("x")));
        Assert.Equal("Handler.Null", nullEx.Code);

        var streamMissingEx = await Assert.ThrowsAsync<HandlerResolutionException>(async () =>
        {
            await foreach (var _ in dispatcher.CreateStream(new CountStream(1)))
            {
            }
        });
        Assert.Equal("Handler.NotFound", streamMissingEx.Code);

        var invalidNotifEx = await Assert.ThrowsAsync<HandlerResolutionException>(() =>
            dispatcher.Publish((object)"not-a-notification"));
        Assert.Equal("Notification.InvalidType", invalidNotifEx.Code);
    }

    private sealed class DelegatingNotificationHandler<T>(Func<T, CancellationToken, Task> callback)
        : INotificationHandler<T>
        where T : INotification
    {
        public Task Handle(T notification, CancellationToken cancellationToken = default) =>
            callback(notification, cancellationToken);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan delta) => now = now.Add(delta);
    }
}
