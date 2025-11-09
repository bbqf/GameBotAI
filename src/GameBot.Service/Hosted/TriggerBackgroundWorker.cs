using GameBot.Domain.Profiles;
using GameBot.Domain.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Hosted;

internal sealed class TriggerBackgroundWorker : BackgroundService
{
    private readonly ILogger<TriggerBackgroundWorker> _logger;
    private readonly TriggerEvaluationService _service;
    private readonly TimeSpan _interval;

    public TriggerBackgroundWorker(
        ILogger<TriggerBackgroundWorker> logger,
        TriggerEvaluationService service)
    {
        _logger = logger;
        _service = service;
        _interval = TimeSpan.FromSeconds(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TriggerBackgroundWorker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // TODO: iterate active profiles/triggers and evaluate
                // This is a stub; evaluation wiring will be completed in later tasks.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during trigger evaluation loop");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
        _logger.LogInformation("TriggerBackgroundWorker stopped");
    }
}
