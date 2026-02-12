using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.Tests.Unit.Performance;

public sealed class RunningSessionsBench
{
    [Fact]
    public void GetRunningSessionsP95Under300ms()
    {
        var sessions = new FakeSessionManager(250);
        var cache = new SessionContextCache();
        var service = new SessionService(sessions, cache);

        var durations = new List<long>();
        for (var i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            var result = service.GetRunningSessions();
            sw.Stop();
            durations.Add(sw.ElapsedMilliseconds);
            result.Should().NotBeNull();
        }

        var ordered = durations.OrderBy(x => x).ToArray();
        var p95Index = (int)Math.Ceiling(0.95 * ordered.Length) - 1;
        var p95 = ordered[Math.Max(p95Index, 0)];

        p95.Should().BeLessThan(300, "running sessions fetch should be responsive");
    }

    private sealed class FakeSessionManager : ISessionManager
    {
        private readonly List<EmulatorSession> _sessions;

        public FakeSessionManager(int count)
        {
            _sessions = Enumerable.Range(0, count).Select(i => new EmulatorSession
            {
                Id = $"sess-{i}",
                GameId = $"game-{i % 5}",
                DeviceSerial = $"emu-{i % 3}",
                Status = SessionStatus.Running,
                Health = SessionHealth.Ok,
                StartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastActivity = DateTimeOffset.UtcNow
            }).ToList();
        }

        public int ActiveCount => _sessions.Count;
        public bool CanCreateSession => true;
        public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotSupportedException();
        public EmulatorSession? GetSession(string id) => _sessions.FirstOrDefault(s => s.Id == id);
        public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions;
        public bool StopSession(string id) => true;
        public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
        public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
    }
}
