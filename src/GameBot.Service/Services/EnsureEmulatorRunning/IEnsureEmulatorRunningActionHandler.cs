using GameBot.Domain.Actions;

namespace GameBot.Service.Services.EnsureEmulatorRunning;

internal interface IEnsureEmulatorRunningActionHandler {
  Task<EnsureEmulatorRunningActionResult> ExecuteAsync(EnsureEmulatorRunningArgs args, CancellationToken ct = default);
}
