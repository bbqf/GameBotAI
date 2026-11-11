using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Security;
using GameBot.UnitTests.Queues;
using Xunit;

namespace GameBot.UnitTests.Security;

public abstract class AuthStoreTestBase : IDisposable {
  protected string Dir { get; } = Path.Combine(Path.GetTempPath(), "gamebot-auth-unit-" + Guid.NewGuid().ToString("N"));

  internal FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

  public void Dispose() {
    if (Directory.Exists(Dir)) {
      Directory.Delete(Dir, recursive: true);
    }

    GC.SuppressFinalize(this);
  }
}

public sealed class RegistrationCodeStoreTests : AuthStoreTestBase {
  private RegistrationCodeStore Store() => new(Dir, Clock);

  [Fact]
  public void CodeHasTheFormatXxxxXxxxFromTheAlphabet() {
    var code = Store().NewCode();

    code.Should().MatchRegex("^[A-Z0-9]{4}-[A-Z0-9]{4}$");
    code.Replace("-", string.Empty, StringComparison.Ordinal).ToCharArray().Should().OnlyContain(c => RegistrationCodeStore.Alphabet.Contains(c, StringComparison.Ordinal));
  }

  [Fact]
  public void OnlyTheHashIsInTheFile() {
    var code = Store().NewCode();

    var text = File.ReadAllText(Path.Combine(Dir, "registration-code.json"));
    text.Should().NotContain(code).And.NotContain(code.Replace("-", string.Empty, StringComparison.Ordinal));
    text.Should().Contain("codeHash");
  }

  [Fact]
  public void CompareIgnoresCaseSpacesAndHyphen() {
    var store = Store();
    var code = store.NewCode();

    store.Check(" " + code.Replace("-", " ", StringComparison.Ordinal), out var hash).Should().Be(CodeCheckResult.Valid);
    hash.Should().NotBeNull();
    RegistrationCodeStore.Normalize(" ab-cd ef ").Should().Be("ABCDEF");
  }

  [Fact]
  public void CodeExpiresAfterTenMinutes() {
    var store = Store();
    var code = store.NewCode();
    Clock.Advance(TimeSpan.FromMinutes(11));

    store.Check(code, out _).Should().Be(CodeCheckResult.Expired);
    File.Exists(Path.Combine(Dir, "registration-code.json")).Should().BeFalse();
  }

  [Fact]
  public void NewCodeCancelsTheOldCode() {
    var store = Store();
    var old = store.NewCode();
    var fresh = store.NewCode();

    store.Check(old, out _).Should().Be(CodeCheckResult.Invalid);
    store.Check(fresh, out _).Should().Be(CodeCheckResult.Valid);
  }

  [Fact]
  public void CorrectUseDeletesTheFile() {
    var store = Store();
    var code = store.NewCode();
    store.Check(code, out var hash);

    store.Consume(hash!).Should().BeTrue();

    File.Exists(Path.Combine(Dir, "registration-code.json")).Should().BeFalse();
    store.Check(code, out _).Should().Be(CodeCheckResult.NoCode);
    store.Consume(hash!).Should().BeFalse();
  }

  [Fact]
  public void FifthFailedAttemptDeletesTheFile() {
    var store = Store();
    var code = store.NewCode();
    for (var i = 0; i < 4; i++) {
      store.Check("AAAA-AAAA", out _).Should().Be(CodeCheckResult.Invalid);
    }

    store.Check("AAAA-AAAA", out _).Should().Be(CodeCheckResult.Invalid);

    File.Exists(Path.Combine(Dir, "registration-code.json")).Should().BeFalse();
    store.Check(code, out _).Should().Be(CodeCheckResult.NoCode);
  }
}

public sealed class PasskeyStoreTests : AuthStoreTestBase {
  private PasskeyStore Store() => new(Dir, Clock);

  [Fact]
  public void AddListAndFindByCredentialId() {
    var store = Store();

    var result = store.Add("cred-1", "key", 0, "My phone", out var added);

    result.Should().Be(PasskeyAddResult.Added);
    added!.Id.Should().HaveLength(8).And.MatchRegex("^[0-9a-f]+$");
    store.List().Should().ContainSingle();
    store.FindByCredentialId("cred-1")!.Name.Should().Be("My phone");
    Store().List().Should().ContainSingle();
  }

  [Fact]
  public void CredentialIdIsUnique() {
    var store = Store();
    store.Add("cred-1", "key", 0, null, out _);

    store.Add("cred-1", "key", 0, null, out _).Should().Be(PasskeyAddResult.Duplicate);
  }

  [Fact]
  public void MaximumTwentyPasskeys() {
    var store = Store();
    for (var i = 0; i < PasskeyStore.MaxPasskeys; i++) {
      store.Add("cred-" + i, "key", 0, null, out _).Should().Be(PasskeyAddResult.Added);
    }

    store.Add("one-more", "key", 0, null, out _).Should().Be(PasskeyAddResult.LimitReached);
  }

  [Fact]
  public void OneUserHandleForAllPasskeys() {
    var store = Store();

    var first = store.GetOrCreateUserHandle();
    var second = Store().GetOrCreateUserHandle();

    first.Should().HaveCount(16);
    second.Should().Equal(first);
  }

  [Fact]
  public void RenameAndRemove() {
    var store = Store();
    store.Add("cred-1", "key", 0, "Old", out var added);

    store.Rename(added!.Id, "  New name ")!.Name.Should().Be("New name");
    store.Rename("unknown", "x").Should().BeNull();
    store.Remove(added.Id).Should().BeTrue();
    store.Remove(added.Id).Should().BeFalse();
    store.List().Should().BeEmpty();
  }

  [Theory]
  [InlineData("", false)]
  [InlineData("   ", false)]
  [InlineData("A", true)]
  [InlineData("0123456789012345678901234567890123456789", true)]
  [InlineData("01234567890123456789012345678901234567890", false)]
  public void NameRules(string name, bool valid) =>
    (PasskeyStore.NormalizeName(name) is not null).Should().Be(valid);

  [Fact]
  public void DefaultNameHasTheRegistrationDate() {
    var store = Store();
    store.Add("cred-1", "key", 0, null, out var added);

    added!.Name.Should().Be("Device 2026-11-10");
  }

  [Fact]
  public void RecordSignInSavesTheCounterAndTime() {
    var store = Store();
    store.Add("cred-1", "key", 0, "x", out var added);

    store.RecordSignIn(added!.Id, 5);

    var saved = Store().List().Single();
    saved.SignCount.Should().Be(5);
    saved.LastUsedUtc.Should().Be(Clock.GetUtcNow());
  }
}

public sealed class SessionStoreTests : AuthStoreTestBase {
  private (SessionStore Sessions, PasskeyStore Passkeys, string PasskeyId) Setup() {
    var passkeys = new PasskeyStore(Dir, Clock);
    passkeys.Add("cred-1", "key", 0, "x", out var added);
    return (new SessionStore(Dir, passkeys, Clock), passkeys, added!.Id);
  }

  [Fact]
  public void CreatedTokenValidatesAndOnlyTheHashIsInTheFile() {
    var (sessions, _, id) = Setup();

    var token = sessions.Create(id);

    sessions.Validate(token)!.PasskeyId.Should().Be(id);
    File.ReadAllText(Path.Combine(Dir, "sessions.json")).Should().NotContain(token);
  }

  [Fact]
  public void UnknownTokenFails() {
    var (sessions, _, _) = Setup();

    sessions.Validate("unknown").Should().BeNull();
    sessions.Validate(null as string).Should().BeNull();
  }

  [Fact]
  public void ExpiredTokenFails() {
    var (sessions, _, id) = Setup();
    var token = sessions.Create(id);
    Clock.Advance(TimeSpan.FromDays(31));

    sessions.Validate(token).Should().BeNull();
  }

  [Fact]
  public void SessionOfARemovedPasskeyFails() {
    var (sessions, passkeys, id) = Setup();
    var token = sessions.Create(id);

    passkeys.Remove(id);

    sessions.Validate(token).Should().BeNull();
  }

  [Fact]
  public void EndForPasskeyEndAndEndAll() {
    var (sessions, passkeys, id) = Setup();
    passkeys.Add("cred-2", "key", 0, "y", out var second);
    var a = sessions.Create(id);
    var b = sessions.Create(second!.Id);
    var c = sessions.Create(id);

    sessions.EndForPasskey(id);
    sessions.Validate(a).Should().BeNull();
    sessions.Validate(c).Should().BeNull();
    sessions.Validate(b).Should().NotBeNull();

    sessions.EndAll();
    sessions.Validate(b).Should().BeNull();
  }

  [Fact]
  public void EachUseMovesTheExpiryAndTheFileIsWrittenAtMostOneTimeInEachHour() {
    var (sessions, _, id) = Setup();
    var token = sessions.Create(id);
    var path = Path.Combine(Dir, "sessions.json");
    var writeAfterCreate = File.GetLastWriteTimeUtc(path);

    Clock.Advance(TimeSpan.FromMinutes(30));
    sessions.Validate(token)!.RefreshCookie.Should().BeFalse();
    File.GetLastWriteTimeUtc(path).Should().Be(writeAfterCreate);

    Clock.Advance(TimeSpan.FromMinutes(40));
    sessions.Validate(token)!.RefreshCookie.Should().BeTrue();
    sessions.Validate(token)!.RefreshCookie.Should().BeFalse();

    Clock.Advance(TimeSpan.FromDays(29));
    sessions.Validate(token).Should().NotBeNull();
  }

  [Fact]
  public void NewInstanceReadsSessionsFromTheFile() {
    var (sessions, passkeys, id) = Setup();
    var token = sessions.Create(id);

    new SessionStore(Dir, passkeys, Clock).Validate(token).Should().NotBeNull();
  }

  [Fact]
  public void ExpiredSessionsGoAtAWrite() {
    var (sessions, _, id) = Setup();
    sessions.Create(id);
    Clock.Advance(TimeSpan.FromDays(31));

    sessions.Create(id);

    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "sessions.json")));
    doc.RootElement.GetProperty("sessions").GetArrayLength().Should().Be(1);
  }
}

public sealed class PendingCeremonyCacheTests {
  private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

  private static PasskeyOptions Options() => new() { PublicKey = JsonSerializer.SerializeToElement(new { challenge = "x" }) };

  [Fact]
  public void ItemExpiresAfterFiveMinutesAndCanBeTakenOneTime() {
    var cache = new PendingCeremonyCache(_clock);
    var id = cache.Add(CeremonyKind.SignIn, "a", Options());

    cache.Take(id, CeremonyKind.SignIn).Should().NotBeNull();
    cache.Take(id, CeremonyKind.SignIn).Should().BeNull();

    var late = cache.Add(CeremonyKind.SignIn, "a", Options());
    _clock.Advance(TimeSpan.FromMinutes(6));
    cache.Take(late, CeremonyKind.SignIn).Should().BeNull();
  }

  [Fact]
  public void KindMustMatch() {
    var cache = new PendingCeremonyCache(_clock);
    var id = cache.Add(CeremonyKind.Registration, "a", Options());

    cache.Take(id, CeremonyKind.SignIn).Should().BeNull();
  }

  [Fact]
  public void SixthItemOfASourceRemovesTheOldestOfThatSource() {
    var cache = new PendingCeremonyCache(_clock);
    var first = cache.Add(CeremonyKind.SignIn, "a", Options());
    for (var i = 0; i < 4; i++) {
      cache.Add(CeremonyKind.SignIn, "a", Options());
    }

    var sixth = cache.Add(CeremonyKind.SignIn, "a", Options());

    cache.Take(first, CeremonyKind.SignIn).Should().BeNull();
    cache.Take(sixth, CeremonyKind.SignIn).Should().NotBeNull();
  }

  [Fact]
  public void TwoHundredItemsFromOneSourceDoNotRemoveTheItemOfAnotherSource() {
    var cache = new PendingCeremonyCache(_clock);
    var owner = cache.Add(CeremonyKind.SignIn, "owner", Options());

    for (var i = 0; i < 200; i++) {
      cache.Add(CeremonyKind.SignIn, "attacker", Options());
    }

    cache.Take(owner, CeremonyKind.SignIn).Should().NotBeNull();
  }

  [Fact]
  public void TotalIsLimitedToOneHundredAndTheBiggestSourceLoses() {
    var cache = new PendingCeremonyCache(_clock);
    for (var s = 0; s < 25; s++) {
      for (var i = 0; i < 4; i++) {
        cache.Add(CeremonyKind.SignIn, "s" + s, Options());
      }
    }

    cache.Count.Should().Be(100);
    var small = cache.Add(CeremonyKind.SignIn, "newcomer", Options());

    cache.Count.Should().Be(100);
    cache.Take(small, CeremonyKind.SignIn).Should().NotBeNull();
  }
}
