using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Catalog;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1305, CA1307, CA1310, CA1812, CA1822

namespace GameBot.UnitTests.Gamepacks;

/// <summary>
/// Feature 147 (T038, FR-015): no hosted service, background service, or timer type depends on a catalog type, directly or
/// through the constructor graph at any depth. The check reads the real registrations. It does not scan source text.
/// </summary>
public sealed class GamepackCatalogNoPollingTests : IDisposable {
  private static readonly Type[] CatalogTypes = {
    typeof(GamepackCatalogService),
    typeof(IGamepackCatalogSource),
    typeof(GamepackCatalogSourceFactory),
    typeof(IGamepackCatalogSettingsStore)
  };

  private readonly string _root = Path.Combine(Path.GetTempPath(), "GameBotCatalogDi", Guid.NewGuid().ToString("N"));

  public GamepackCatalogNoPollingTests() => Directory.CreateDirectory(_root);

  public void Dispose() {
    try {
      Directory.Delete(_root, recursive: true);
    }
    catch (IOException) {
      // A temporary folder that stays is not a test failure.
    }
  }

  [Fact]
  public void NoBackgroundTypeOfTheRealRegistrationReachesTheCatalog() {
    var builder = WebApplication.CreateBuilder();
    builder.Configuration["Service:Storage:Root"] = _root;
    GameBot.Service.GameBotServiceSetup.ConfigureServices(builder);

    var paths = FindPaths(builder.Services);

    paths.Should().BeEmpty("no hosted service or timer may use the catalog (FR-015)");
    builder.Services.Any(d => d.ServiceType == typeof(GamepackCatalogService)).Should().BeTrue("the real registration holds the catalog service");
  }

  [Fact]
  public void ANegativeControlThroughAnIntermediateClassIsFound() {
    var services = new ServiceCollection();
    services.AddSingleton<GamepackCatalogService>(_ => null!);
    services.AddSingleton<ControlIntermediate>();
    services.AddHostedService<ControlHosted>();

    var paths = FindPaths(services);

    paths.Should().ContainSingle().Which.Should().Contain(nameof(ControlIntermediate)).And.Contain(nameof(GamepackCatalogService));
  }

  [Fact]
  public void AnUnrelatedHostedServiceIsNotFlagged() {
    var services = new ServiceCollection();
    services.AddHostedService<CleanHosted>();

    FindPaths(services).Should().BeEmpty();
  }

  private static List<string> FindPaths(IServiceCollection services) {
    var descriptors = services.ToList();
    var found = new List<string>();
    foreach (var root in descriptors.Where(IsBackgroundRegistration)) {
      var type = root.ImplementationType ?? root.ImplementationInstance?.GetType() ?? root.ServiceType;
      Walk(type, type.Name, new HashSet<Type>(), descriptors, found);
    }

    return found;
  }

  private static bool IsBackgroundRegistration(ServiceDescriptor descriptor) {
    var type = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
    return descriptor.ServiceType == typeof(IHostedService)
      || (type is not null && (typeof(IHostedService).IsAssignableFrom(type) || typeof(BackgroundService).IsAssignableFrom(type) || HoldsTimer(type)));
  }

  private static bool HoldsTimer(Type type) =>
    type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
      .Any(f => f.FieldType == typeof(System.Threading.Timer) || f.FieldType == typeof(PeriodicTimer));

  private static void Walk(Type type, string path, HashSet<Type> visited, List<ServiceDescriptor> descriptors, List<string> found) {
    if (CatalogTypes.Any(c => c.IsAssignableFrom(type))) {
      found.Add(path);
      return;
    }

    if (!visited.Add(type)) {
      return;
    }

    foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
      foreach (var parameter in constructor.GetParameters()) {
        var parameterType = Unwrap(parameter.ParameterType);
        Walk(parameterType, path + " -> " + parameterType.Name, visited, descriptors, found);
        foreach (var impl in descriptors.Where(d => d.ServiceType == parameterType)) {
          var implementation = impl.ImplementationType ?? impl.ImplementationInstance?.GetType();
          if (implementation is not null && implementation != parameterType) {
            Walk(implementation, path + " -> " + implementation.Name, visited, descriptors, found);
          }
        }
      }
    }
  }

  private static Type Unwrap(Type type) =>
    type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? type.GetGenericArguments()[0] : type;

  private sealed class ControlIntermediate {
    public ControlIntermediate(GamepackCatalogService service) => Service = service;

    public GamepackCatalogService Service { get; }
  }

  private sealed class ControlHosted : IHostedService {
    public ControlHosted(ControlIntermediate intermediate) => Intermediate = intermediate;

    public ControlIntermediate Intermediate { get; }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
  }

  private sealed class CleanHosted : IHostedService {
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
  }
}
