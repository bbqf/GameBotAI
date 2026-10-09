using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// Tells if the bot runs from the folder that the installer made (FR-015, research R-012). The MSI writes
/// the registry value <c>GameBot</c> under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. The value
/// is a quoted path to the EXE. The bot is "installed here" when the folder of that EXE is the folder of
/// the service assembly. A bot that runs from source has no such value.
/// </summary>
internal sealed class InstallLocationGuard : IInstallLocationGuard {
  private readonly Func<string?> _readRunValue;
  private readonly string _serviceFolder;

  public InstallLocationGuard()
    : this(ReadRunValueFromRegistry, AppContext.BaseDirectory) { }

  public InstallLocationGuard(Func<string?> readRunValue, string serviceFolder) {
    _readRunValue = readRunValue;
    _serviceFolder = serviceFolder;
  }

  public bool IsInstalledHere() {
    var value = _readRunValue();
    if (string.IsNullOrWhiteSpace(value)) {
      return false;
    }

    var exePath = value.Trim().Trim('"');
    string? exeFolder;
    try {
      exeFolder = Path.GetDirectoryName(exePath);
    }
    catch (ArgumentException) {
      return false;
    }

    if (string.IsNullOrEmpty(exeFolder)) {
      return false;
    }

    return string.Equals(Normalize(exeFolder), Normalize(_serviceFolder), StringComparison.OrdinalIgnoreCase);
  }

  private static string Normalize(string folder) {
    try {
      return Path.GetFullPath(folder).TrimEnd('\\', '/');
    }
    catch (ArgumentException) {
      return folder.TrimEnd('\\', '/');
    }
  }

  [ExcludeFromCodeCoverage]
  private static string? ReadRunValueFromRegistry() {
    if (!OperatingSystem.IsWindows()) {
      return null;
    }

    return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "GameBot", null)?.ToString();
  }
}
