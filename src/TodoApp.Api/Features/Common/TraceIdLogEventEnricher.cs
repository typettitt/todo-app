using System.Diagnostics;

using Serilog.Core;
using Serilog.Events;

namespace TodoApp.Api.Features.Common;

internal sealed class TraceIdLogEventEnricher : ILogEventEnricher
{
    public const string TraceIdPropertyName = "TraceId";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(TraceIdPropertyName, traceId));
    }
}
