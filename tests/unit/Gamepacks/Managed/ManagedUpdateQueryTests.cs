using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Service.Endpoints.Dto;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T018): the route parameter binding.</summary>
public sealed class ManagedUpdateQueryTests {
  [Fact]
  public void TheDefaultsAreNoDryRunAllTemplatesAndNotManaged() {
    var query = ManagedUpdateQuery.From(null, null, null);

    query.DryRun.Should().BeFalse();
    query.Templates.Should().BeNull();
    query.Managed.Should().BeFalse();
  }

  [Fact]
  public void TheTemplatesValuesAreRepeatableAndBlankValuesAreDropped() {
    var query = ManagedUpdateQuery.From(true, new[] { "Daily", " ", "Weekly ", string.Empty }, true);

    query.DryRun.Should().BeTrue();
    query.Templates.Should().Equal("Daily", "Weekly");
    query.Managed.Should().BeTrue();
  }

  [Fact]
  public void OnlyBlankTemplatesGiveAllTemplates() {
    ManagedUpdateQuery.From(false, new[] { " " }).Templates.Should().BeNull();
  }

  [Fact]
  public void TheQueryMapsToAnUpdateRequestAndToARollbackRequest() {
    var query = ManagedUpdateQuery.From(true, new[] { "A" });

    var update = query.ToUpdateRequest("{}", "sig");
    var rollback = query.ToRollbackRequest("pns");

    update.Mode.Should().Be(ManagedUpdateMode.Update);
    update.FileText.Should().Be("{}");
    update.SignatureText.Should().Be("sig");
    update.DryRun.Should().BeTrue();
    update.Templates.Should().Equal("A");
    rollback.Mode.Should().Be(ManagedUpdateMode.Rollback);
    rollback.GamepackName.Should().Be("pns");
    rollback.FileText.Should().BeNull();
  }

  [Fact]
  public void ACatalogBodyWithNoNameOrVersionHasNullValues() {
    var body = new GamepackCatalogUpdateRequest();

    body.Name.Should().BeNull();
    body.Version.Should().BeNull();
    body.DryRun.Should().BeFalse();
    body.Templates.Should().BeNull();
  }
}
