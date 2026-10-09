namespace GameBot.Updater;

/// <summary>The effects of the updater: processes and files. Tests replace it with a fake.</summary>
internal interface IUpdaterHost {
  /// <summary>Wait for the process to exit. Return false when it still runs after the time-out. A process that does not exist counts as exited.</summary>
  bool WaitForExit(int pid, TimeSpan timeout);

  /// <summary>Run msiexec with the arguments and wait. Return the exit code.</summary>
  Task<int> RunMsiexecAsync(string arguments);

  /// <summary>Start the bot from the install folder. Return false when the start fails.</summary>
  bool StartBot(string installFolder);

  /// <summary>Write the result file. The write must be safe if the bot reads the file at the same time.</summary>
  void WriteResultFile(string path, string json);

  DateTimeOffset UtcNow { get; }
}
