using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Notifications;

/// <summary>Feature 120 (V-20, FR-016, SC-006): the token is in no response, no log line and no error body.</summary>
public sealed class NotificationTokenMaskingTests {
  private const string Token = NotificationContractHost.Token;

  private static Uri Rel(string path) => NotificationContractHost.Rel(path);

  [Fact]
  public async Task V20_NoResponseNoLogLineAndNoErrorBodyHoldsTheToken() {
    using var host = new NotificationContractHost();
    var bodies = new System.Collections.Generic.List<string>();

    var create = await host.Client.PostAsJsonAsync(Rel("/api/notifications/targets"), new {
      type = "telegram", name = "Phone", settings = new { chatId = "-100" }, secrets = new { botToken = Token }
    });
    bodies.Add(await create.Content.ReadAsStringAsync());
    var id = System.Text.Json.JsonDocument.Parse(bodies[0]).RootElement.GetProperty("id").GetString()!;

    bodies.Add(await (await host.Client.GetAsync(Rel("/api/notifications/targets"))).Content.ReadAsStringAsync());
    bodies.Add(await (await host.Client.PutAsJsonAsync(Rel($"/api/notifications/targets/{id}"), new {
      type = "telegram", name = "Phone 2", settings = new { chatId = "-101" }
    })).Content.ReadAsStringAsync());
    bodies.Add(await (await host.Client.GetAsync(Rel("/api/notifications/types"))).Content.ReadAsStringAsync());

    // A test send that fails with each kind of fault. No fault text may hold the token.
    host.Telegram.Answer(HttpStatusCode.Unauthorized, $"{{\"ok\":false,\"description\":\"Unauthorized {Token}\"}}");
    bodies.Add(await (await host.Client.PostAsync(Rel($"/api/notifications/targets/{id}/test"), null)).Content.ReadAsStringAsync());
    host.Telegram.Answer(HttpStatusCode.InternalServerError, "oops");
    host.Telegram.Answer(HttpStatusCode.InternalServerError, "oops");
    bodies.Add(await (await host.Client.PostAsync(Rel($"/api/notifications/targets/{id}/test"), null)).Content.ReadAsStringAsync());

    // Error bodies of bad requests that repeat the token in the request.
    foreach (var bad in new[] {
      $"{{\"type\":\"telegram\",\"name\":\"x\",\"settings\":{{\"chatId\":\"abc\"}},\"secrets\":{{\"botToken\":\"{Token}\"}}}}",
      $"{{\"type\":\"telegram\",\"name\":\"x\",\"settings\":{{\"chatId\":\"1\",\"botToken\":\"{Token}\"}}}}",
      $"{{\"type\":\"telegram\",\"name\":\"\",\"secrets\":{{\"botToken\":\"{Token}\"}}}}",
      $"{{\"type\":\"telegram\",\"name\":\"x\",\"settings\":{{\"chatId\":\"1\",\"{Token}\":\"y\"}},\"secrets\":{{\"botToken\":\"{Token}\"}}}}",
      $"{{\"type\":\"telegram\",\"secrets\":{{\"{Token}\":\"{Token}\"}}"
    }) {
      var response = await host.Client.PostAsync(Rel("/api/notifications/targets"), new StringContent(bad, Encoding.UTF8, "application/json"));
      response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
      bodies.Add(await response.Content.ReadAsStringAsync());
    }

    bodies.Should().NotContain(b => b.Contains(Token, StringComparison.Ordinal));
    // The key that the client chose can echo in an "unknown" message. The token as a key is the one edge.
    host.Logs.Lines.Should().NotContain(l => l.Contains(Token, StringComparison.Ordinal));
    host.Logs.Lines.Should().NotContain(l => l.Contains("/bot", StringComparison.Ordinal) && l.Contains("sendMessage", StringComparison.Ordinal));
  }
}
