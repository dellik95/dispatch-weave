using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DispatchWeave.Diagnostics;

/// <summary>
/// OpenTelemetry-compatible <see cref="ActivitySource"/> and <see cref="Meter"/> definitions for DispatchWeave.
/// </summary>
public static class DispatchWeaveDiagnostics
{
    public const string ActivitySourceName = "DispatchWeave";
    public const string MeterName = "DispatchWeave";
    public const string Version = "2.0.0";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);
    public static readonly Meter Meter = new(MeterName, Version);

    public static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        "dispatchweave.requests.count",
        unit: "{request}",
        description: "Total number of requests dispatched through DispatchWeave.");

    public static readonly Counter<long> RequestsFailed = Meter.CreateCounter<long>(
        "dispatchweave.requests.failures",
        unit: "{failure}",
        description: "Total number of failed requests dispatched through DispatchWeave.");

    public static readonly Histogram<double> RequestDurationMs = Meter.CreateHistogram<double>(
        "dispatchweave.requests.duration",
        unit: "ms",
        description: "Execution duration of requests in milliseconds.");
}
