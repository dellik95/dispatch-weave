# DispatchWeave

Production-grade CQRS request & stream dispatch, ordered extensible pipelines, resilience behaviors, and polymorphic notification publishing for **.NET 11**.

## Packages

| Package | Description |
| :--- | :--- |
| `DispatchWeave.Abstractions` | Lightweight contracts (`IRequest`, `IStreamRequest`, `INotification`, `IDispatcher`, `ISender`, `IPublisher`, `IDispatchRoutes`, `IDispatchRegistrar`, `ICommand`, `IQuery`, processor & resilience interfaces). |
| `DispatchWeave` | Core runtime (`Dispatcher`, container-agnostic `DispatchWeaveBuilder`, `ServiceProviderRoutes`, `StandaloneDispatchRegistrar`, explicit registries, built-in behaviors, notification publishers, OpenTelemetry diagnostics). |
| `DispatchWeave.DependencyInjection` | `IServiceCollection.AddDispatchWeave(...)` integration via `ServiceCollectionRegistrar`. |
| `DispatchWeave.Autofac` | `ContainerBuilder.RegisterDispatchWeave(...)` integration via `AutofacDispatchRegistrar`. |
| `DispatchWeave.SimpleInjector` | `Container.AddDispatchWeave(...)` integration via `SimpleInjectorDispatchRegistrar`. |

Install from the [dellik95 GitHub Packages feed](https://nuget.pkg.github.com/dellik95/index.json).

---

## Unified Container-Agnostic Builder

`DispatchWeaveBuilder` is completely independent of any specific DI container. You use the **exact same fluent builder and extension methods** whether you run on Microsoft.Extensions.DependencyInjection, Autofac, SimpleInjector, a custom `IDispatchRegistrar`, or completely standalone without a DI container:

```csharp
using DispatchWeave;
using DispatchWeave.Results;
using FluentResults;

// 1. Microsoft.Extensions.DependencyInjection
services.AddDispatchWeave(b => ConfigureWeave(b));

// 2. Autofac
containerBuilder.RegisterDispatchWeave(b => ConfigureWeave(b));

// 3. SimpleInjector
container.AddDispatchWeave(b => ConfigureWeave(b));

// 4. Standalone (No DI container required)
IDispatcher dispatcher = DispatchWeaveBuilder.Create()
    .Apply(ConfigureWeave)
    .Build();

static void ConfigureWeave(DispatchWeaveBuilder builder)
{
    builder
        .RegisterHandlersFromAssemblyContaining<GetName>()
        .WithTelemetry()
        .WithLogging()
        .WithExceptionHandling()
        .WithPreAndPostProcessors()
        .WithValidation()
        .WithCaching()
        .WithTimeout(TimeSpan.FromSeconds(10))
        .WithRetry(defaultMaxRetries: 2)
        .WithNotificationStrategy(NotificationPublishStrategy.ParallelWhenAll);
}
```

To plug in any other DI container, implement the 4-method `IDispatchRegistrar` interface and pass it to `new DispatchWeaveBuilder(registrar)`, or wrap a resolution delegate in `new ServiceProviderRoutes(type => container.Resolve(type))`.

---

## Requests, Commands, Queries & Streaming

### Standard & Result-based Requests

```csharp
// Result-oriented Query / Command (FluentResults)
public sealed record GetName(string Name) : IQuery<string>, ICacheableRequest
{
    public string CacheKey => $"name:{Name}";
    public TimeSpan? TimeToLive => TimeSpan.FromMinutes(5);
}

public sealed class GetNameHandler : IQueryHandler<GetName, string>
{
    public Task<Result<string>> Handle(GetName request, CancellationToken token)
        => Task.FromResult(Result.Ok(request.Name));
}

// Void Request returning Unit
public sealed record AuditPing(string Message) : IRequest;

public sealed class AuditPingHandler : IRequestHandler<AuditPing>
{
    public Task Handle(AuditPing request, CancellationToken token)
        => Task.CompletedTask;
}
```

### Async Streaming (`IStreamRequest<T>` / `IStreamQuery<T>`)

```csharp
public sealed record StreamMetrics(int Count) : IStreamRequest<int>;

public sealed class StreamMetricsHandler : IStreamRequestHandler<StreamMetrics, int>
{
    public async IAsyncEnumerable<int> Handle(
        StreamMetrics request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 1; i <= request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

await foreach (var item in sender.CreateStream(new StreamMetrics(5), token))
{
    // Process streamed items
}
```

---

## Built-in Pipeline Behaviors & Extensibility Points

- **Validation**:
  - `.WithValidation()` registers `ValidationBehavior<TRequest, TResponse>` for `FluentResults` responses (`Result` / `Result<T>`), attaching `PropertyName`, `ErrorCode`, and `Severity` metadata.
  - `.WithThrowingValidation()` registers `ThrowingValidationBehavior<TRequest, TResponse>` for arbitrary response types, throwing `ValidationException`.
- **Pre/Post Processors** (`.WithPreAndPostProcessors()`):
  - Implement `IRequestPreProcessor<TRequest>` and `IRequestPostProcessor<TRequest, TResponse>` (automatically discovered by `RegisterHandlers`).
- **Exception Handling & Actions** (`.WithExceptionHandling()`):
  - Implement `IRequestExceptionAction<TRequest, TException>` for side-effects (auditing, metrics) before rethrowing.
  - Implement `IRequestExceptionHandler<TRequest, TResponse, TException>` and call `state.SetHandled(fallbackResponse)` to recover with a fallback response.
- **Caching** (`.WithCaching(IDispatchCache? cache = null)`):
  - Requests implementing `ICacheableRequest` are cached automatically via `IDispatchCache` (defaults to thread-safe `InMemoryDispatchCache` with `TimeProvider` TTL support). Failed `Result` responses are never cached.
- **Timeout & Retry** (`.WithTimeout(...)` / `.WithRetry(...)`):
  - Implement `ITimeoutRequest` or configure `Options.DefaultTimeout`.
  - Implement `IRetryableRequest` (`MaxRetries`, `Delay`, `ShouldRetry`) or configure `Options.DefaultMaxRetries`.
- **Logging & OpenTelemetry** (`.WithLogging()` / `.WithTelemetry()`):
  - Structured request logging via `ILogger<LoggingBehavior<TRequest, TResponse>>`.
  - Distributed tracing (`ActivitySource("DispatchWeave")`) and metrics (`Meter("DispatchWeave")`: `dispatchweave.requests.count`, `dispatchweave.requests.failures`, `dispatchweave.requests.duration`) via `DispatchWeaveDiagnostics`.
- **Pipeline Ordering & Inherited Behaviors**:
  - Annotate behaviors with `[PipelineOrder(-100)]` or implement `IOrderedBehavior`. Lower numbers execute earlier (outermost).
  - Behaviors registered for base request interfaces/classes automatically wrap compatible derived requests (`Options.IncludeInheritedBehaviors = true`).

---

## Polymorphic Notifications & Publishing Strategies

Publishing an event dispatches to handlers for the concrete event type, its base classes, and any implemented `INotification` interfaces (`Options.PolymorphicNotificationDispatch = true`).

Choose a built-in strategy via `builder.WithNotificationStrategy(...)` or supply a custom `INotificationPublisher`:
- `NotificationPublishStrategy.Sequential` / `StopOnFirstException` (default)
- `NotificationPublishStrategy.ContinueAndAggregateExceptions` (invokes all handlers and aggregates failures into `AggregateException`)
- `NotificationPublishStrategy.ParallelWhenAll` (concurrent execution via `Task.WhenAll`)
- `NotificationPublishStrategy.FireAndForget` (background execution with `Options.FireAndForgetExceptionHandler`)
