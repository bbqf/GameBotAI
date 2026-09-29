using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Queues;
using Xunit;
using Xunit.Abstractions;

namespace GameBot.Tests.Unit.Performance;

/// <summary>
/// Feature 078 performance budget (plan.md, constitution IV): scope resolution plus substitution must
/// stay under 1 ms per step dispatch.
/// <para>
/// The budget is generous by design — every step this precedes performs device I/O (an ADB round
/// trip, a screen capture, or a template match) costing tens to hundreds of milliseconds, so the
/// parameter work is meant to be invisible on the hot path. These tests exist to catch a regression
/// that changes that, e.g. rebuilding the scope chain per field instead of per step.
/// </para>
/// </summary>
public sealed class ParameterScopeBench {
  private const int Iterations = 2000;
  private readonly ITestOutputHelper _output;

  public ParameterScopeBench(ITestOutputHelper output) => _output = output;

  private static ParameterScope BuildDeepScope() {
    // The deepest realistic chain: queue built-ins -> template entry -> sequence -> command -> loop.
    var queue = new ExecutionQueue {
      Id = "q",
      Name = "Q",
      EmulatorSerial = "emulator-5558",
      EmulatorInstanceName = "PNS-1",
      EmulatorInstanceIndex = 1,
      LinkedGameId = "game-7"
    };

    var entryBindings = new Collection<ParameterBinding> {
      new() { Name = "adbSerial", Value = "emulator-5560" },
      new() { Name = "slot", Value = "two" }
    };
    var commandDeclarations = new Collection<ParameterDeclaration> {
      new() { Name = "adbSerial" },
      new() { Name = "waitMs", Type = ParameterValueType.Number, Default = "5000" }
    };

    return ParameterScope.FromQueue(queue)
        .Child(ParameterScopeLayers.Entry, entryBindings, null)
        .Child(ParameterScopeLayers.Sequence, null, null)
        .Child(ParameterScopeLayers.Command, null, commandDeclarations)
        .WithIteration(3);
  }

  private static double MeasureAverageMicroseconds(Action action) {
    // Warm up so JIT and the compiled regex are not charged to the measurement.
    for (var i = 0; i < 200; i++) action();

    var sw = Stopwatch.StartNew();
    for (var i = 0; i < Iterations; i++) action();
    sw.Stop();

    return sw.Elapsed.TotalMilliseconds * 1000.0 / Iterations;
  }

  [Fact]
  public void ResolvingAcrossTheFullScopeChainStaysWellUnderTheBudget() {
    var scope = BuildDeepScope();

    var averageMicroseconds = MeasureAverageMicroseconds(() => {
      scope.TryResolve("adbSerial", out _);
      scope.TryResolve("waitMs", out _);
      scope.TryResolve(ParameterNameRules.QueueEmulatorSerial, out _);
    });

    averageMicroseconds.Should().BeLessThan(1000,
        "three resolutions across a five-layer chain must stay inside the 1 ms per-step budget");
  }

  [Fact]
  public void ResolvingAnAbsentNameIsNotPathological() {
    // The miss path walks every layer twice (bindings, then defaults), so it is the worst case.
    var scope = BuildDeepScope();

    var averageMicroseconds = MeasureAverageMicroseconds(() => scope.TryResolve("nothingSuppliesThis", out _));

    averageMicroseconds.Should().BeLessThan(1000, "a miss walks the whole chain and must still be cheap");
  }

  [Fact]
  public void ResolvingAWholeCommandStepStaysUnderOneMillisecond() {
    var scope = BuildDeepScope();
    var step = new CommandStep {
      Type = CommandStepType.EnsureEmulatorRunning,
      Order = 0,
      EnsureEmulatorRunning = new EnsureEmulatorRunningConfig {
        AdbSerial = "{{adbSerial}}",
        InstanceName = "PNS-{{slot}}"
      },
      FieldTemplates = new Dictionary<string, string> {
        ["ensureEmulatorRunning.instanceIndex"] = "{{queue.instanceIndex}}"
      }
    };

    var averageMicroseconds = MeasureAverageMicroseconds(() => {
      CommandStepResolver.TryResolve(step, scope, out _, out _, out _).Should().BeTrue();
    });

    averageMicroseconds.Should().BeLessThan(1000,
        "resolving one step's parameters must be negligible beside the device I/O that follows it");
  }

  [Fact]
  public void UnparametrizedStepsTakeTheFastPathAndCostAlmostNothing() {
    // The overwhelming majority of stored steps are unparametrized, so this path must not regress:
    // it short-circuits before building a substitution context or cloning the step.
    var scope = BuildDeepScope();
    var step = new CommandStep {
      Type = CommandStepType.KeyInput,
      Order = 0,
      KeyInput = new KeyInputConfig { Key = "KEYCODE_HOME" }
    };

    var averageMicroseconds = MeasureAverageMicroseconds(() => {
      CommandStepResolver.TryResolve(step, scope, out var resolved, out _, out _);
      ReferenceEquals(resolved, step).Should().BeTrue("the fast path returns the stored step as-is");
    });

    averageMicroseconds.Should().BeLessThan(100,
        "an unparametrized step must be an order of magnitude cheaper than the budget");
  }

  /// <summary>
  /// Feature 115: one <see cref="ParameterScope.TryBindChild"/> call with a whole placeholder, a
  /// value with text around a placeholder, and a literal. The goal is less than 50 µs on average.
  /// The assert uses the 1 ms step budget, so that a slow build host does not make the test fail.
  /// The test writes the measured average to the test output for the performance note.
  /// </summary>
  [Fact]
  public void BindingAChildScopeWithPlaceholdersIsCheap() {
    // Five layers with twenty names in total.
    var scope = ParameterScope.Empty;
    string[] layers = {
      ParameterScopeLayers.Queue, ParameterScopeLayers.Entry, ParameterScopeLayers.Sequence,
      ParameterScopeLayers.Command, ParameterScopeLayers.Command
    };
    for (var layer = 0; layer < layers.Length; layer++) {
      var bindings = new Collection<ParameterBinding>();
      for (var name = 0; name < 4; name++) {
        bindings.Add(new ParameterBinding { Name = $"p{layer}{name}", Value = $"value-{layer}-{name}" });
      }
      scope = scope.Child(layers[layer], bindings, null);
    }

    var stepBindings = new Collection<ParameterBinding> {
      new() { Name = "whole", Value = "{{p13}}" },
      new() { Name = "mixed", Value = "nova-{{p02}}-{{p41}}" },
      new() { Name = "literal", Value = "pns-todo-radar" }
    };

    var averageMicroseconds = MeasureAverageMicroseconds(() => {
      scope.TryBindChild(ParameterScopeLayers.Command, stepBindings, out _, out _).Should().BeTrue();
    });

    _output.WriteLine($"TryBindChild average: {averageMicroseconds:F2} µs (goal: < 50 µs)");
    averageMicroseconds.Should().BeLessThan(1000,
        "binding the parameters of one step must be negligible beside the device I/O that follows it");
  }

  [Fact]
  public void FlatteningTheScopeForSubstitutionIsCheap() {
    var scope = BuildDeepScope();

    var averageMicroseconds = MeasureAverageMicroseconds(() => scope.ToSubstitutionContext());

    averageMicroseconds.Should().BeLessThan(1000, "flattening happens once per substituted step");
  }
}
