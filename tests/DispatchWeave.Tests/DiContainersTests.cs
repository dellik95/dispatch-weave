using Autofac;
using DispatchWeave.Autofac;
using DispatchWeave.Results;
using DispatchWeave.SimpleInjector;
using FluentResults;
using FluentValidation;
using SimpleInjector;
using Xunit;

namespace DispatchWeave.Tests;

public class DiContainersTests
{
    public sealed record GreetQuery(string Name) : IQuery<string>, ICacheableRequest
    {
        public string CacheKey => $"greet:{Name}";
    }

    public sealed class GreetQueryHandler : IQueryHandler<GreetQuery, string>
    {
        public Task<Result<string>> Handle(GreetQuery request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Ok($"Hello, {request.Name}!"));
    }

    public sealed class GreetQueryValidator : AbstractValidator<GreetQuery>
    {
        public GreetQueryValidator() =>
            RuleFor(x => x.Name).NotEmpty().WithErrorCode("Name.Required");
    }

    [Fact]
    public async Task Autofac_dispatches_requests_streams_validation_caching_and_polymorphic_notifications()
    {
        var log = new FeaturesAndBehaviorsTests.NotificationTestLog();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(log).AsSelf().SingleInstance();
        builder.RegisterType<GreetQueryValidator>().As<IValidator<GreetQuery>>().InstancePerLifetimeScope();

        builder.RegisterDispatchWeave(b => b
            .RegisterHandlersFromAssemblyContaining<GreetQuery>()
            .WithValidation()
            .WithCaching()
            .WithPreAndPostProcessors());

        using var container = builder.Build();
        using var scope = container.BeginLifetimeScope();

        var dispatcher = scope.Resolve<IDispatcher>();
        var sender = scope.Resolve<ISender>();
        var publisher = scope.Resolve<IPublisher>();

        // 1. Valid request
        var ok = await sender.Send(new GreetQuery("Autofac"));
        Assert.True(ok.IsSuccess);
        Assert.Equal("Hello, Autofac!", ok.Value);

        // 2. Invalid request short-circuited by ValidationBehavior
        var invalid = await sender.Send(new GreetQuery(""));
        Assert.True(invalid.IsFailed);
        Assert.Equal("Name.Required", Assert.Single(invalid.Errors).Metadata["ErrorCode"]);

        // 3. Streaming request
        var streamItems = new List<int>();
        await foreach (var item in dispatcher.CreateStream(new FeaturesAndBehaviorsTests.CountStream(3)))
            streamItems.Add(item);
        Assert.Equal([1, 2, 3], streamItems);

        // 4. Polymorphic notifications
        await publisher.Publish(new FeaturesAndBehaviorsTests.UserRegisteredEvent("AutofacUser"));
        Assert.Equal(
            ["concrete:AutofacUser", "base:AutofacUser", "iface:AutofacUser"],
            log);
    }

    [Fact]
    public async Task SimpleInjector_dispatches_requests_streams_validation_caching_and_polymorphic_notifications()
    {
        var log = new FeaturesAndBehaviorsTests.NotificationTestLog();
        using var container = new Container();
        container.RegisterInstance(log);
        container.RegisterInstance(new List<string>());
        container.Register<IValidator<GreetQuery>, GreetQueryValidator>(Lifestyle.Transient);

        container.AddDispatchWeave(b => b
            .RegisterHandlersFromAssemblyContaining<GreetQuery>(
                t => t.DeclaringType == typeof(DiContainersTests) || t.DeclaringType == typeof(FeaturesAndBehaviorsTests))
            .WithValidation()
            .WithCaching()
            .WithPreAndPostProcessors());

        var dispatcher = container.GetInstance<IDispatcher>();
        var sender = container.GetInstance<ISender>();
        var publisher = container.GetInstance<IPublisher>();

        // 1. Valid request
        var ok = await sender.Send(new GreetQuery("SimpleInjector"));
        Assert.True(ok.IsSuccess);
        Assert.Equal("Hello, SimpleInjector!", ok.Value);

        // 2. Invalid request short-circuited by ValidationBehavior
        var invalid = await sender.Send(new GreetQuery(""));
        Assert.True(invalid.IsFailed);
        Assert.Equal("Name.Required", Assert.Single(invalid.Errors).Metadata["ErrorCode"]);

        // 3. Streaming request
        var streamItems = new List<int>();
        await foreach (var item in dispatcher.CreateStream(new FeaturesAndBehaviorsTests.CountStream(3)))
            streamItems.Add(item);
        Assert.Equal([1, 2, 3], streamItems);

        // 4. Polymorphic notifications
        await publisher.Publish(new FeaturesAndBehaviorsTests.UserRegisteredEvent("SIUser"));
        Assert.Equal(
            ["concrete:SIUser", "base:SIUser", "iface:SIUser"],
            log);
    }

    [Fact]
    public async Task Universal_ServiceProviderRoutes_delegate_adapter_works_with_custom_factory()
    {
        var handler = new GreetQueryHandler();
        var routes = new ServiceProviderRoutes(type =>
        {
            if (type == typeof(IRequestHandler<GreetQuery, Result<string>>))
                return handler;
            return null;
        });

        var dispatcher = new Dispatcher(routes);
        var result = await dispatcher.Send(new GreetQuery("CustomFactory"));
        Assert.Equal("Hello, CustomFactory!", result.Value);
    }

    [Fact]
    public async Task Standalone_DispatchWeaveBuilder_works_without_any_DI_container()
    {
        var log = new FeaturesAndBehaviorsTests.NotificationTestLog();

        var dispatcher = DispatchWeaveBuilder.Create()
            .AddService(log)
            .AddValidator(new GreetQueryValidator())
            .RegisterHandlersFromAssemblyContaining<GreetQuery>(
                t => t.DeclaringType == typeof(DiContainersTests) || t.DeclaringType == typeof(FeaturesAndBehaviorsTests))
            .WithValidation()
            .WithCaching()
            .WithTimeout(TimeSpan.FromSeconds(5))
            .WithRetry(defaultMaxRetries: 1)
            .Build();

        // 1. Valid request
        var ok = await dispatcher.Send(new GreetQuery("Standalone"));
        Assert.True(ok.IsSuccess);
        Assert.Equal("Hello, Standalone!", ok.Value);

        // 2. Invalid request short-circuited by ValidationBehavior
        var invalid = await dispatcher.Send(new GreetQuery(""));
        Assert.True(invalid.IsFailed);
        Assert.Equal("Name.Required", Assert.Single(invalid.Errors).Metadata["ErrorCode"]);

        // 3. Streaming request
        var streamItems = new List<int>();
        await foreach (var item in dispatcher.CreateStream(new FeaturesAndBehaviorsTests.CountStream(3)))
            streamItems.Add(item);
        Assert.Equal([1, 2, 3], streamItems);

        // 4. Polymorphic notifications
        await dispatcher.Publish(new FeaturesAndBehaviorsTests.UserRegisteredEvent("NoDiUser"));
        Assert.Equal(
            ["concrete:NoDiUser", "base:NoDiUser", "iface:NoDiUser"],
            log);
    }
}
