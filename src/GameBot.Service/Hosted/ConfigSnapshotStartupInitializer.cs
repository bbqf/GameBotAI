using GameBot.Service.Services;

namespace GameBot.Service.Hosted;

internal sealed class ConfigSnapshotStartupInitializer : IHostedService
{
    private readonly IConfigSnapshotService _svc;

    public ConfigSnapshotStartupInitializer(IConfigSnapshotService svc)
    {
        _svc = svc;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _svc.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Ignore failures on startup; errors are handled within the service per FR-013
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
