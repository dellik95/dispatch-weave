using DispatchWeave.Notifications;

namespace DispatchWeave;

/// <summary>
/// Built-in execution strategies for publishing notifications to multiple handlers.
/// </summary>
public enum NotificationPublishStrategy
{
    /// <summary>
    /// Executes handlers sequentially in registration order and stops on the first exception.
    /// </summary>
    Sequential = 0,

    /// <summary>
    /// Alias for <see cref="Sequential"/>: executes handlers one by one and stops immediately when an exception occurs.
    /// </summary>
    StopOnFirstException = Sequential,

    /// <summary>
    /// Executes all handlers sequentially even if some throw, aggregating all thrown exceptions into an <see cref="AggregateException"/>.
    /// </summary>
    ContinueAndAggregateExceptions = 1,

    /// <summary>
    /// Executes all handlers concurrently using <see cref="Task.WhenAll(IEnumerable{Task})"/>.
    /// </summary>
    ParallelWhenAll = 2,

    /// <summary>
    /// Schedules handlers to run asynchronously in the background without blocking the caller.
    /// </summary>
    FireAndForget = 3
}

/// <summary>
/// Configuration options controlling <see cref="Dispatcher"/>, pipelines, behaviors, and notification publishing.
/// </summary>
public sealed class DispatchWeaveOptions
{
    /// <summary>
    /// The notification publishing strategy to use when no explicit <see cref="CustomNotificationPublisher"/> is provided.
    /// Defaults to <see cref="NotificationPublishStrategy.Sequential"/>.
    /// </summary>
    public NotificationPublishStrategy NotificationPublishStrategy { get; set; } = NotificationPublishStrategy.Sequential;

    /// <summary>
    /// Optional custom <see cref="INotificationPublisher"/> taking precedence over <see cref="NotificationPublishStrategy"/>.
    /// </summary>
    public INotificationPublisher? CustomNotificationPublisher { get; set; }

    /// <summary>
    /// Optional callback invoked when a background handler throws during <see cref="NotificationPublishStrategy.FireAndForget"/> publishing.
    /// </summary>
    public Action<Exception, INotification>? FireAndForgetExceptionHandler { get; set; }

    /// <summary>
    /// Whether behaviors registered for base request interfaces or base classes automatically apply to derived requests.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool IncludeInheritedBehaviors { get; set; } = true;

    /// <summary>
    /// Whether publishing a notification also invokes handlers registered for its base types and implemented <see cref="INotification"/> interfaces.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool PolymorphicNotificationDispatch { get; set; } = true;

    /// <summary>
    /// Whether the last registered pipeline behavior should wrap earlier behaviors as the outermost layer.
    /// Defaults to <see langword="false"/> (first registered behavior is outermost).
    /// </summary>
    public bool LastBehaviorOutermost { get; set; }

    /// <summary>
    /// Whether pipeline behaviors should be sorted by <see cref="IOrderedBehavior"/> and <see cref="PipelineOrderAttribute"/>.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool EnablePipelineOrdering { get; set; } = true;

    /// <summary>
    /// Optional global fallback timeout applied by <see cref="Behaviors.TimeoutBehavior{TRequest, TResponse}"/> when a request does not implement <see cref="ITimeoutRequest"/>.
    /// </summary>
    public TimeSpan? DefaultTimeout { get; set; }

    /// <summary>
    /// Optional global fallback max retries applied by <see cref="Behaviors.RetryBehavior{TRequest, TResponse}"/> when a request does not implement <see cref="IRetryableRequest"/>.
    /// </summary>
    public int DefaultMaxRetries { get; set; }

    /// <summary>
    /// Default delay between retry attempts when not specified by <see cref="IRetryableRequest.Delay"/>.
    /// </summary>
    public TimeSpan DefaultRetryDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Default time-to-live for cached responses when <see cref="ICacheableRequest.TimeToLive"/> is <see langword="null"/>.
    /// </summary>
    public TimeSpan? DefaultCacheTimeToLive { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Resolves the active <see cref="INotificationPublisher"/> based on the explicit parameter or configured options.
    /// </summary>
    public INotificationPublisher ResolvePublisher(INotificationPublisher? explicitPublisher = null)
    {
        if (explicitPublisher is not null)
            return explicitPublisher;

        if (CustomNotificationPublisher is not null)
            return CustomNotificationPublisher;

        return NotificationPublishStrategy switch
        {
            NotificationPublishStrategy.ContinueAndAggregateExceptions => ContinueOnExceptionNotificationPublisher.Instance,
            NotificationPublishStrategy.ParallelWhenAll => ParallelNotificationPublisher.Instance,
            NotificationPublishStrategy.FireAndForget => new FireAndForgetNotificationPublisher(FireAndForgetExceptionHandler),
            _ => SequentialNotificationPublisher.Instance
        };
    }
}
