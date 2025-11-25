using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Triggers.Evaluators;
using Xunit;

namespace GameBot.IntegrationTests.TextOcr;

public sealed class TesseractProcessOcrProcessTests
{
    [Fact]
    public void RecognizeInvokesStubProcessAndLogsInvocation()
    {
        using var bitmap = new Bitmap(width: 4, height: 4);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.FillRectangle(Brushes.Black, 0, 0, 4, 4);

        var logger = new RecordingInvocationLogger();
        var sut = new TesseractProcessOcr(FakeTesseractExecutable.Resolve(), "eng", psm: null, oem: null, invocationLogger: logger);

        var result = sut.Recognize(bitmap);

        result.Text.Should().StartWith("STUB_eng_", "stub echoes the language");
        result.Confidence.Should().BeGreaterThan(0.5);

        logger.Invocations.Should().ContainSingle();
        var invocation = logger.Invocations.Single();
        invocation.ExePath.Should().EndWith("TesseractStub" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        invocation.Arguments.Should().Contain("-l");
        invocation.Arguments.Should().Contain("eng");
        invocation.StdOut.WasTruncated.Should().BeTrue("stub emits >8KB stdout");
        invocation.StdOut.Content.Length.Should().Be(8 * 1024);
        invocation.StdErr.WasTruncated.Should().BeFalse();
        invocation.ExitCode.Should().Be(0);
        invocation.StdErr.Content.Should().Contain("psm");
    }

    [Fact]
    public void InvocationContextRoundTripsAllProperties()
    {
        var stdout = new TesseractInvocationCapture("stdout", false);
        var stderr = new TesseractInvocationCapture("stderr", true);
        var started = DateTimeOffset.UtcNow;
        var completed = started.AddSeconds(1);
        var args = new ReadOnlyCollection<string>(new List<string> { "a", "b" });
        var env = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?> { ["k"] = "v" });

        var context = new TesseractInvocationContext(
            InvocationId: Guid.NewGuid(),
            ExePath: "stub",
            Arguments: args,
            WorkingDirectory: "work",
            EnvironmentOverrides: env,
            StartedAtUtc: started,
            CompletedAtUtc: completed,
            ExitCode: 42,
            StdOut: stdout,
            StdErr: stderr);

        context.InvocationId.Should().NotBe(Guid.Empty);
        context.ExePath.Should().Be("stub");
        context.Arguments.Should().BeEquivalentTo(args);
        context.WorkingDirectory.Should().Be("work");
        context.EnvironmentOverrides.Should().ContainKey("k");
        context.StartedAtUtc.Should().Be(started);
        context.CompletedAtUtc.Should().Be(completed);
        context.ExitCode.Should().Be(42);
        context.StdOut.Should().Be(stdout);
        context.StdErr.Should().Be(stderr);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("abc123", 1)]
    [InlineData("**abc**", 3d / 7d)]
    public void ComputeConfidenceProducesExpectedRatios(string? text, double expected)
    {
        var result = TesseractProcessOcr.ComputeConfidence(text);
        result.Should().BeApproximately(expected, 1e-6);
    }

    private sealed class RecordingInvocationLogger : ITesseractInvocationLogger
    {
        public List<TesseractInvocationContext> Invocations { get; } = new();

        public void Log(in TesseractInvocationContext context)
        {
            Invocations.Add(context);
        }
    }

    private static class FakeTesseractExecutable
    {
        public static string Resolve()
        {
            var repoRoot = FindRepositoryRoot();
            var configuration = GetConfigurationName();
            var tfm = GetTargetFrameworkName();
            var exeName = "TesseractStub" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
            var candidate = Path.Combine(repoRoot, "tests", "TestAssets", "TesseractStub", "bin", configuration, tfm, exeName);
            if (!File.Exists(candidate))
            {
                throw new FileNotFoundException($"Unable to locate stub executable at '{candidate}'. Ensure the TesseractStub project is built before running tests.");
            }
            return candidate;
        }

        private static string FindRepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var solutionPath = Path.Combine(dir.FullName, "GameBot.sln");
                if (File.Exists(solutionPath))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Could not locate repository root starting from " + AppContext.BaseDirectory);
        }

        private static string GetConfigurationName()
        {
            var tfmDir = new DirectoryInfo(AppContext.BaseDirectory);
            var configurationDir = tfmDir.Parent ?? throw new InvalidOperationException("Missing configuration directory");
            return configurationDir.Name;
        }

        private static string GetTargetFrameworkName()
        {
            var tfmDir = new DirectoryInfo(AppContext.BaseDirectory);
            return tfmDir.Name;
        }
    }
}
