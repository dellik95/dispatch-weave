using DispatchWeave.Results;
using FluentResults;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DispatchWeave.Tests;

public class DispatchTests
{
    private sealed record Echo(string Text) : IQuery<string>;
    private sealed class EchoHandler : IQueryHandler<Echo, string>
    {
        public Task<Result<string>> Handle(Echo request, CancellationToken token) => Task.FromResult(Result.Ok(request.Text));
    }

    private sealed class Validator : AbstractValidator<Echo>
    {
        public Validator() => RuleFor(x => x.Text).NotEmpty().WithErrorCode("Required");
    }

    private sealed record Changed : INotification;
    private sealed class Listener(List<string> events) : INotificationHandler<Changed>
    {
        public Task Handle(Changed notification, CancellationToken token)
        {
            events.Add("changed");
            return Task.CompletedTask;
        }
    }

    private sealed class CommonBehavior : IPipelineBehavior<IQuery<string>, Result<string>>
    {
        public Task<Result<string>> Handle(IQuery<string> request, RequestHandlerDelegate<Result<string>> next,
            CancellationToken cancellationToken) => next();
    }

    [Fact]
    public async Task Validation_can_short_circuit_an_explicit_route_before_handler_resolution()
    {
        var pipelines = new Registration.PipelineRegistry();
        pipelines.AddBehavior<Echo, Result<string>>(new ValidationBehavior<Echo, Result<string>>([new Validator()]));
        var dispatcher = new Dispatcher(new Registration.RegistryRoutes(new(), pipelines, new()));

        var result = await dispatcher.Send(new Echo(""));
        Assert.Equal("Required", Assert.Single(result.Errors).Metadata["ErrorCode"]);
    }

    [Fact]
    public void Registry_can_select_behaviors_registered_for_a_base_request_contract()
    {
        var pipelines = new Registration.PipelineRegistry();
        var behavior = new CommonBehavior();
        pipelines.AddBehavior<IQuery<string>, Result<string>>(behavior);
        Assert.Same(behavior, Assert.Single(pipelines.GetInheritedBehaviors<Echo, Result<string>>()));
        Assert.Empty(pipelines.GetInheritedBehaviors<IQuery<string>, Result<string>>());
    }

    [Fact]
    public async Task Registry_resolves_a_fresh_handler_from_the_factory_for_each_call()
    {
        var handlers = new Registration.HandlerRegistry();
        var count = 0;
        handlers.Register<Echo, Result<string>>(() =>
        {
            count++;
            return new EchoHandler();
        });
        var dispatcher = new Dispatcher(new Registration.RegistryRoutes(handlers, new(), new()));
        await dispatcher.Send(new Echo("first"));
        await dispatcher.Send(new Echo("second"));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Missing_handler_exposes_a_stable_error_code()
    {
        var dispatcher = new Dispatcher(new Registration.RegistryRoutes(new(), new(), new()));
        var failure = await Assert.ThrowsAsync<HandlerResolutionException>(() => dispatcher.Send(new Echo("missing")));
        Assert.Equal("Handler.NotFound", failure.Code);
    }

    [Fact]
    public async Task Dispatches_closed_requests_and_returns_structured_validation_errors()
    {
        var services = new ServiceCollection();
        services.AddScoped<IValidator<Echo>, Validator>();
        services.AddDispatchWeave(b => b.RegisterHandlers(typeof(Echo).Assembly).WithValidation());
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        Assert.Equal("hello", (await dispatcher.Send(new Echo("hello"))).Value);
        var failure = Assert.Single((await dispatcher.Send(new Echo(""))).Errors);
        Assert.Equal("Text", failure.Metadata["PropertyName"]);
        Assert.Equal("Required", failure.Metadata["ErrorCode"]);
    }

    [Fact]
    public async Task Repeated_scans_publish_once_to_the_current_scope_for_a_base_typed_event()
    {
        var services = new ServiceCollection();
        services.AddScoped<List<string>>();
        for (int i = 0; i < 2; i++)
            services.AddDispatchWeave(b => b.RegisterHandlers(typeof(Changed).Assembly));
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        INotification notification = new Changed();
        await scope.ServiceProvider.GetRequiredService<IDispatcher>().Publish(notification);
        Assert.Equal(["changed"], scope.ServiceProvider.GetRequiredService<List<string>>());
    }
}
