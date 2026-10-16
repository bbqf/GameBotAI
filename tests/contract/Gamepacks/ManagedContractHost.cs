using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.UnitTests.Gamepacks.Managed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable CA2007, CA1861, CA1707, CA1002, CA1819, CA2227, CA2000, CA1305, CA1307, CA1310, CA1849, CA1054, CA2234

namespace GameBot.ContractTests.Gamepacks;

/// <summary>A contract host whose queue port is a fake (feature 148). A test builds queues and templates through the stores.</summary>
internal sealed class ManagedContractHost : IDisposable {
  public ManagedContractHost(bool withThrowingGate = false, Action<IServiceCollection, string>? configure = null) {
    Host = new GamepackContractHost(withThrowingGate, (services, root) => {
      services.RemoveAll<IManagedQueueControl>();
      services.AddSingleton<IManagedQueueControl>(sp => new FakeManagedQueueControl(sp.GetRequiredService<IQueueRepository>()));
      configure?.Invoke(services, root);
    });
  }

  public GamepackContractHost Host { get; }

  public FakeManagedQueueControl Control => (FakeManagedQueueControl)Host.Services.GetRequiredService<IManagedQueueControl>();

  public System.Net.Http.HttpClient Client => Host.Client;

  public void Dispose() => Host.Dispose();

  public static string Pack(string version, params string[] sequenceNames) => ManagedTestRig.Pack(version, sequenceNames);

  public async Task<(string TemplateId, string QueueId)> AddTemplateAndQueueAsync(string templateName, bool running, params string[] sequenceNames) {
    var template = new QueueTemplate { Id = Guid.NewGuid().ToString("N"), Name = templateName };
    foreach (var name in sequenceNames) {
      template.Entries.Add(new QueueTemplateEntry { SequenceId = await Host.SequenceIdAsync(name) });
    }

    await Host.Services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template);
    var queue = await Host.Services.GetRequiredService<IQueueRepository>().CreateAsync(new ExecutionQueue {
      Id = Guid.NewGuid().ToString("N"),
      Name = "Q-" + templateName,
      EmulatorSerial = "emulator-5554",
      LinkedTemplateId = template.Id
    });
    if (running) {
      Control.Running.Add(queue.Id);
    }

    return (template.Id, queue.Id);
  }

  public async Task<QueueTemplate> TemplateAsync(string name) =>
    (await Host.Services.GetRequiredService<IQueueTemplateRepository>().FindByNameAsync(name))!;

  private static string Query(bool dryRun, IEnumerable<string>? templates, bool? managed = null) {
    var parts = new List<string>();
    if (dryRun) {
      parts.Add("dryRun=true");
    }

    if (managed == true) {
      parts.Add("managed=true");
    }

    parts.AddRange((templates ?? Array.Empty<string>()).Select(t => "templates=" + Uri.EscapeDataString(t)));
    return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
  }

  public Task<HttpResponseMessage> UpdateAsync(string json, bool dryRun = false, IEnumerable<string>? templates = null) =>
    Client.PostAsync(new Uri("/api/gamepacks/update" + Query(dryRun, templates), UriKind.Relative), new StringContent(json, Encoding.UTF8, "application/json"));

  public Task<HttpResponseMessage> CatalogUpdateAsync(string? name, string? version, bool dryRun = false, IEnumerable<string>? templates = null) =>
    Client.PostAsJsonAsync(new Uri("/api/gamepacks/catalog/update", UriKind.Relative), new { name, version, dryRun, templates });

  public Task<HttpResponseMessage> ManagedRollbackAsync(string name, bool dryRun = false, IEnumerable<string>? templates = null) =>
    Client.PostAsync(new Uri($"/api/gamepacks/{name}/rollback" + Query(dryRun, templates, managed: true), UriKind.Relative), content: null);

  public static async Task<string> ReasonOfAsync(HttpResponseMessage response) =>
    (await GamepackContractHost.ReadAsync(response)).GetProperty("reason").GetString()!;
}
