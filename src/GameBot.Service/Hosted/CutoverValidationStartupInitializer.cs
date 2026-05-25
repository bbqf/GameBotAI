using GameBot.Service.StartupValidation;

namespace GameBot.Service.Hosted;

internal sealed class CutoverValidationStartupInitializer : IHostedService {
  private static readonly Action<ILogger, DateTime, string, Exception?> LogCutoverFailed =
    LoggerMessage.Define<DateTime, string>(LogLevel.Critical, new EventId(54100, nameof(LogCutoverFailed)), "Cutover validation failed at {CheckedAtUtc:u}. Blocking legacy references:\n{Details}");

  private readonly ILegacyActionReferenceScanner _scanner;
  private readonly ILogger<CutoverValidationStartupInitializer> _logger;

  public CutoverValidationStartupInitializer(ILegacyActionReferenceScanner scanner, ILogger<CutoverValidationStartupInitializer> logger) {
    ArgumentNullException.ThrowIfNull(scanner);
    ArgumentNullException.ThrowIfNull(logger);
    _scanner = scanner;
    _logger = logger;
  }

  public async Task StartAsync(CancellationToken cancellationToken) {
    var report = await _scanner.ScanAsync(cancellationToken).ConfigureAwait(false);
    if (!report.IsBlocked) {
      return;
    }

    var details = string.Join(Environment.NewLine, report.Issues.Select(issue => $"- {issue.Store}:{issue.Path} ({issue.ReferenceCount}) {issue.Message}"));
    LogCutoverFailed(_logger, report.CheckedAtUtc, details, null);
    throw new InvalidOperationException($"Cutover validation failed. Legacy Action references remain:\n{details}");
  }

  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}