using System;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Logging;

internal static class LoggingBuilderExtensions
{
    public static ILoggingBuilder AddRuntimeLoggingGate(this ILoggingBuilder builder, LoggingPolicyGate gate)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(gate);

        builder.AddFilter(gate.ShouldLog);
        return builder;
    }
}