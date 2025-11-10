using System.Drawing;
using GameBot.Emulator.Session;
using GameBot.Domain.Profiles.Evaluators;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests;

public class AdbScreenSourceTests
{
    private sealed class DummySessionManager : ISessionManager
    {
        public int ActiveCount => 0;
        public bool CanCreateSession => false;
        public EmulatorSession CreateSession(string gameIdOrPath, string? profileId = null, string? preferredDeviceSerial = null) => throw new NotImplementedException();
        public EmulatorSession? GetSession(string id) => null;
        public bool StopSession(string id) => false;
        public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
        public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
    }

    [Fact(DisplayName="AdbScreenSource returns null with no sessions")]
    public void ReturnsNullWithoutSessions()
    {
        var src = new AdbScreenSource(new DummySessionManager(), NullLogger<AdbScreenSource>.Instance, NullLogger<GameBot.Emulator.Adb.AdbClient>.Instance);
        var bmp = src.GetLatestScreenshot();
        Assert.Null(bmp);
    }
}
