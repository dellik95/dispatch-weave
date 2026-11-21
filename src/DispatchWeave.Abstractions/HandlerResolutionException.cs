namespace DispatchWeave;

public sealed class HandlerResolutionException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;

    public static HandlerResolutionException Missing(Type request) =>
        new("Handler.NotFound", $"No handler registered for {request.Name}.");

    public static HandlerResolutionException NullFactory(Type request) =>
        new("Handler.Null", $"Handler factory produced null for {request.Name}.");

    public static HandlerResolutionException InvalidHandlerType(Type request, Type actual) =>
        new("Handler.InvalidType", $"Resolved instance '{actual.FullName}' is not a valid handler for '{request.Name}'.");

    public static HandlerResolutionException InvalidNotification(Type notification) =>
        new("Notification.InvalidType", $"Type '{notification.FullName}' does not implement INotification.");
}
