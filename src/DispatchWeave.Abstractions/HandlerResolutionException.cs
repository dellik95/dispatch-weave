namespace DispatchWeave;

public sealed class HandlerResolutionException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;

    public static HandlerResolutionException Missing(Type request) =>
        new("Handler.NotFound", $"No handler registered for {request.Name}.");

    public static HandlerResolutionException NullFactory(Type request) =>
        new("Handler.Null", $"Handler factory produced null for {request.Name}.");
}
