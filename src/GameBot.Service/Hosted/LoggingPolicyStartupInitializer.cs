using GameBot.Domain.Services.Logging;

namespace GameBot.Service.Hosted;

internal sealed class LoggingPolicyStartupInitializer : IHostedService
{
    private readonly IRuntimeLoggingPolicyService _service;

    public LoggingPolicyStartupInitializer(IRuntimeLoggingPolicyService service)
    {
        _service = service;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _service.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Swallow to avoid blocking startup; service logs failures via repository/service layers.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}