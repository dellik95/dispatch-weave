# DispatchWeave

DispatchWeave routes .NET requests to scoped handlers, runs behaviors in registration order, and publishes notifications sequentially to handlers for the concrete event type.

Install `DispatchWeave.DependencyInjection` from the [dellik95 GitHub Packages feed](https://nuget.pkg.github.com/dellik95/index.json). Reference `DispatchWeave.Abstractions` in projects that only declare contracts. To supply a custom handler factory or registry, implement `IDispatchRoutes` and pass it to `Dispatcher`.

```csharp
using DispatchWeave;
using DispatchWeave.Results;
using FluentResults;

services.AddDispatchWeave(builder => builder.RegisterHandlers(typeof(GetName).Assembly).WithValidation());

public sealed record GetName(string Name) : IQuery<string>;
public sealed class GetNameHandler : IQueryHandler<GetName, string>
{
    public Task<Result<string>> Handle(GetName request, CancellationToken token)
        => Task.FromResult(Result.Ok(request.Name));
}
```

Register FluentValidation validators as `IValidator<TRequest>`. Validation runs before the handler and returns FluentResults errors with property names and error codes. Custom response types can define their own validation behavior.

Version 1.1.1 introduces the explicit registries and typed handler resolution errors. The 1.1.0 package contains the 1.0.0 source.

### Explicit registration

For standalone dispatch, register a typed handler factory with `DispatchWeave.Registration.HandlerRegistry.Register<TRequest, TResponse>`. Pass the handler, pipeline and notification registries to `RegistryRoutes`, then construct a `Dispatcher` with those routes. Factories run per dispatch. An optional `INotificationPublisher` controls execution and error handling for subscribers.

To include a behavior registered for a base request interface in an explicit registry, override `RegistryRoutes.Behaviors<TRequest, TResponse>` and compose `GetInheritedBehaviors<TRequest, TResponse>()` with the base pipeline. Exact and open registrations keep their existing order. Handler factories execute when the pipeline reaches the handler, so a short-circuiting behavior can complete a request without creating one.
