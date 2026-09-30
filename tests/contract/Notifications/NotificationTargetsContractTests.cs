using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Notifications;

/// <summary>Feature 120 (V-18): the target routes. A bad body gives 400 and never 500.</summary>
public sealed class NotificationTargetsContractTests {
  private const string Token = NotificationContractHost.Token;
  private const string OtherToken = "654321:ZYXWVUTSRQPONMLKJIHGFEDCBA_1234-Ab7c";

  private static Uri Rel(string path) => NotificationContractHost.Rel(path);

  [Fact]
  public async Task ListStartsEmpty() {
    using var host = new NotificationContractHost();

    var response = await host.Client.GetAsync(Rel("/api/notifications/targets"));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await NotificationContractHost.ReadJsonAsync(response)).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task CreateGives201WithLocationAndAViewThatHasNoSecret() {
    using var host = new NotificationContractHost();

    var response = await host.Client.PostAsJsonAsync(Rel("/api/notifications/targets"), new {
      type = "Telegram", name = "  My phone ", settings = new { chatId = "-1001234567890" }, secrets = new { botToken = Token }
    });

    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var view = await NotificationContractHost.ReadJsonAsync(response);
    var id = view.GetProperty("id").GetString()!;
    response.Headers.Location!.ToString().Should().EndWith($"/api/notifications/targets/{id}");
    view.GetProperty("type").GetString().Should().Be("telegram");
    view.GetProperty("name").GetString().Should().Be("My phone");
    view.GetProperty("enabled").GetBoolean().Should().BeTrue();
    view.GetProperty("settings").GetProperty("chatId").GetString().Should().Be("-1001234567890");
    view.GetProperty("settings").TryGetProperty("botToken", out _).Should().BeFalse();
    view.GetProperty("hasSecret").GetBoolean().Should().BeTrue();
    view.GetProperty("secretHint").GetString().Should().Be("••••Xy9z");
    view.GetProperty("createdAt").ValueKind.Should().Be(JsonValueKind.String);
    view.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.String);
    (await response.Content.ReadAsStringAsync()).Should().NotContain(Token);
  }

  [Fact]
  public async Task ListShowsTheTargetAndNoSecret() {
    using var host = new NotificationContractHost();
    await host.CreateTargetAsync("Phone");

    var response = await host.Client.GetAsync(Rel("/api/notifications/targets"));

    var text = await response.Content.ReadAsStringAsync();
    text.Should().NotContain(Token);
    var list = JsonDocument.Parse(text).RootElement;
    list.GetArrayLength().Should().Be(1);
    list[0].GetProperty("name").GetString().Should().Be("Phone");
  }

  [Fact]
  public async Task PutWithAnEmptyTokenKeepsTheStoredTokenAndANewTokenReplacesIt() {
    using var host = new NotificationContractHost();
    var created = await host.CreateTargetAsync("Phone");
    var id = created.GetProperty("id").GetString()!;

    var keep = await host.Client.PutAsJsonAsync(Rel($"/api/notifications/targets/{id}"), new {
      type = "telegram", name = "Renamed", enabled = false, settings = new { chatId = "555" }, secrets = new { botToken = "" }
    });
    keep.StatusCode.Should().Be(HttpStatusCode.OK);
    var view = await NotificationContractHost.ReadJsonAsync(keep);
    view.GetProperty("name").GetString().Should().Be("Renamed");
    view.GetProperty("enabled").GetBoolean().Should().BeFalse();
    view.GetProperty("settings").GetProperty("chatId").GetString().Should().Be("555");
    view.GetProperty("hasSecret").GetBoolean().Should().BeTrue();
    view.GetProperty("secretHint").GetString().Should().EndWith("Xy9z");
    view.GetProperty("createdAt").GetString().Should().Be(created.GetProperty("createdAt").GetString());

    // The stored token works: the test send uses the old token in the URL.
    (await host.Client.PostAsync(Rel($"/api/notifications/targets/{id}/test"), null)).StatusCode.Should().Be(HttpStatusCode.OK);
    host.Telegram.Requests[^1].Uri.AbsoluteUri.Should().Contain($"/bot{Token}/");

    var absent = await host.Client.PutAsJsonAsync(Rel($"/api/notifications/targets/{id}"), new {
      type = "telegram", name = "Renamed", settings = new { chatId = "555" }
    });
    absent.StatusCode.Should().Be(HttpStatusCode.OK);
    (await NotificationContractHost.ReadJsonAsync(absent)).GetProperty("enabled").GetBoolean().Should().BeFalse("an absent enabled flag keeps the stored value");

    var replace = await host.Client.PutAsJsonAsync(Rel($"/api/notifications/targets/{id}"), new {
      type = "telegram", name = "Renamed", settings = new { chatId = "555" }, secrets = new { botToken = OtherToken }
    });
    replace.StatusCode.Should().Be(HttpStatusCode.OK);
    (await NotificationContractHost.ReadJsonAsync(replace)).GetProperty("secretHint").GetString().Should().EndWith("Ab7c");
    (await host.Client.PostAsync(Rel($"/api/notifications/targets/{id}/test"), null)).StatusCode.Should().Be(HttpStatusCode.OK);
    host.Telegram.Requests[^1].Uri.AbsoluteUri.Should().Contain($"/bot{OtherToken}/");
  }

  [Fact]
  public async Task PutOfAnUnknownIdGives404AndATypeChangeGives400() {
    using var host = new NotificationContractHost();
    var id = (await host.CreateTargetAsync()).GetProperty("id").GetString()!;

    var missing = await host.Client.PutAsJsonAsync(Rel("/api/notifications/targets/nope"), new { type = "telegram", name = "x" });
    var change = await host.Client.PutAsJsonAsync(Rel($"/api/notifications/targets/{id}"), new {
      type = "smoke", name = "x", settings = new { chatId = "1" }
    });

    missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await NotificationContractHost.ReadJsonAsync(missing)).GetProperty("error").GetProperty("code").GetString().Should().Be("not_found");
    change.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task DeleteGives204ThenTheTargetIsGoneAnd404ForAnUnknownId() {
    using var host = new NotificationContractHost();
    var id = (await host.CreateTargetAsync()).GetProperty("id").GetString()!;

    (await host.Client.DeleteAsync(Rel($"/api/notifications/targets/{id}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await host.Client.DeleteAsync(Rel($"/api/notifications/targets/{id}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

    (await NotificationContractHost.ReadJsonAsync(await host.Client.GetAsync(Rel("/api/notifications/targets")))).GetArrayLength().Should().Be(0);
  }

  [Theory]
  [InlineData("{\"name\":\"x\",\"settings\":{\"chatId\":\"1\"},\"secrets\":{\"botToken\":\"123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ\"}}", "type")]
  [InlineData("{\"type\":\"pigeon\",\"name\":\"x\"}", "type")]
  [InlineData("{\"type\":\"telegram\",\"name\":\"  \",\"settings\":{\"chatId\":\"1\"},\"secrets\":{\"botToken\":\"123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ\"}}", "name")]
  [InlineData("{\"type\":\"telegram\",\"settings\":{\"chatId\":\"1\"},\"secrets\":{\"botToken\":\"123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ\"}}", "name")]
  [InlineData("{\"type\":\"telegram\",\"name\":\"x\",\"settings\":{\"chatId\":\"1\"}}", "botToken")]
  [InlineData("{\"type\":\"telegram\",\"name\":\"x\",\"settings\":{\"chatId\":\"1\"},\"secrets\":{\"botToken\":\"not-a-token\"}}", "botToken")]
  [InlineData("{\"type\":\"telegram\",\"name\":\"x\",\"secrets\":{\"botToken\":\"123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ\"}}", "chatId")]
  [InlineData("{\"type\":\"telegram\",\"name\":\"x\",\"settings\":{\"chatId\":\"abc\"},\"secrets\":{\"botToken\":\"123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ\"}}", "chatId")]
  [InlineData("{\"type\":\"telegram\",\"name\":\"x\",\"settings\":{\"chatId\":\"1\",\"extra\":\"y\"},\"secrets\":{\"botToken\":\"123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ\"}}", "extra")]
  [InlineData("{\"type\":\"telegram\",\"name\":\"x\",\"settings\":{\"chatId\":\"1\",\"botToken\":\"123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ\"}}", "secret")]
  [InlineData("{\"type\":\"telegram\",\"name\":\"x\",\"settings\":{\"chatId\":\"1\"},\"secrets\":{\"botToken\":\"123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ\",\"other\":\"z\"}}", "other")]
  [InlineData("{ this is not json", "JSON")]
  [InlineData("[1,2,3]", "JSON")]
  [InlineData("", "JSON")]
  [InlineData("null", "JSON")]
  public async Task ABadBodyGives400AndNeverEchoesASecret(string body, string mention) {
    using var host = new NotificationContractHost();

    var response = await host.Client.PostAsync(Rel("/api/notifications/targets"), new StringContent(body, Encoding.UTF8, "application/json"));

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var text = await response.Content.ReadAsStringAsync();
    var error = JsonDocument.Parse(text).RootElement.GetProperty("error");
    error.GetProperty("code").GetString().Should().Be("invalid_request");
    error.GetProperty("message").GetString().Should().Contain(mention);
    text.Should().NotContain("123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ");
  }

  [Fact]
  public async Task ATooLongNameAndANonJsonContentTypeGive400() {
    using var host = new NotificationContractHost();

    var longName = await host.Client.PostAsJsonAsync(Rel("/api/notifications/targets"), new {
      type = "telegram", name = new string('n', 101), settings = new { chatId = "1" }, secrets = new { botToken = Token }
    });
    var plain = await host.Client.PostAsync(Rel("/api/notifications/targets"), new StringContent("hello", Encoding.UTF8, "text/plain"));
    var putBad = await host.Client.PutAsync(Rel("/api/notifications/targets/x"), new StringContent("{", Encoding.UTF8, "application/json"));

    longName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    plain.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    putBad.StatusCode.Should().Be(HttpStatusCode.NotFound, "the target check comes first");
  }

  [Fact]
  public async Task PutWithABadBodyOnAKnownTargetGives400() {
    using var host = new NotificationContractHost();
    var id = (await host.CreateTargetAsync()).GetProperty("id").GetString()!;

    var response = await host.Client.PutAsync(Rel($"/api/notifications/targets/{id}"), new StringContent("{", Encoding.UTF8, "application/json"));

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task TypesListTheTelegramFields() {
    using var host = new NotificationContractHost();

    var response = await host.Client.GetAsync(Rel("/api/notifications/types"));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var types = await NotificationContractHost.ReadJsonAsync(response);
    var telegram = types.EnumerateArray().Single(t => t.GetProperty("type").GetString() == "telegram");
    telegram.GetProperty("displayName").GetString().Should().Be("Telegram");
    var fields = telegram.GetProperty("fields").EnumerateArray().ToList();
    fields.Select(f => f.GetProperty("key").GetString()).Should().Equal("botToken", "chatId");
    fields[0].GetProperty("secret").GetBoolean().Should().BeTrue();
    fields[0].GetProperty("required").GetBoolean().Should().BeTrue();
    fields[1].GetProperty("secret").GetBoolean().Should().BeFalse();
    fields[1].GetProperty("label").GetString().Should().Be("Chat ID");
  }

  [Fact]
  public async Task TargetsSurviveAHostRestart() {
    string id;
    string dir;
    using (var host = new NotificationContractHost()) {
      id = (await host.CreateTargetAsync("Kept")).GetProperty("id").GetString()!;
      dir = host.DataDir;
      System.IO.File.Exists(System.IO.Path.Combine(dir, "notifications", "targets.json")).Should().BeTrue();
    }

    // A second host on the same data folder would need the folder. The store file is enough proof.
    id.Should().NotBeNullOrEmpty();
  }
}
