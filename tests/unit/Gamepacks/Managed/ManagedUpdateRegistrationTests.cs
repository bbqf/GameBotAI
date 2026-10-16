using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861, CA1305, CA1307, CA1310, CA1812, CA1822

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T017): the real registration holds the managed update services.</summary>
public sealed class ManagedUpdateRegistrationTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "GameBotManagedDi", Guid.NewGuid().ToString("N"));

  public ManagedUpdateRegistrationTests() => Directory.CreateDirectory(_root);

  public void Dispose() {
    try {
      Directory.Delete(_root, recursive: true);
    }
    catch (IOException) {
      // A temporary folder that stays is not a test failure.
    }
  }

  private WebApplicationBuilder Builder() {
    var builder = WebApplication.CreateBuilder();
    builder.Configuration["Service:Storage:Root"] = _root;
    GameBot.Service.GameBotServiceSetup.ConfigureServices(builder);
    return builder;
  }

  [Fact]
  public void TheOrchestratorIsASingletonAndTheOptionsHaveTheSixtySecondLimit() {
    var builder = Builder();

    var orchestrator = builder.Services.Single(d => d.ServiceType == typeof(GamepackUpdateOrchestrator));
    builder.Services.Single(d => d.ServiceType == typeof(IManagedQueueControl)).Lifetime.Should().Be(ServiceLifetime.Singleton);
    builder.Services.Single(d => d.ServiceType == typeof(IManagedBackupStore)).Lifetime.Should().Be(ServiceLifetime.Singleton);
    orchestrator.Lifetime.Should().Be(ServiceLifetime.Singleton);
    ((ManagedUpdateOptions)builder.Services.Single(d => d.ServiceType == typeof(ManagedUpdateOptions)).ImplementationInstance!)
      .StopTimeout.Should().Be(TimeSpan.FromSeconds(60));
  }

  [Fact]
  public void TheServicesResolveFromTheContainerAndTheOrchestratorIsOneInstance() {
    var builder = Builder();
    using var app = builder.Build();

    var first = app.Services.GetRequiredService<GamepackUpdateOrchestrator>();
    var second = app.Services.GetRequiredService<GamepackUpdateOrchestrator>();

    first.Should().BeSameAs(second);
    app.Services.GetRequiredService<IManagedQueueControl>().Should().NotBeNull();
    app.Services.GetRequiredService<IManagedBackupStore>().Should().BeOfType<ManagedBackupStore>();
  }
}
