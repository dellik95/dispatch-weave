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
