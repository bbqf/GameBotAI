using FluentAssertions;
using GameBot.Domain.Actions;
using Xunit;

namespace GameBot.UnitTests.Domain;

public sealed class PrimitiveActionValidationServiceTests {
  [Fact]
  public void ValidateReturnsNoErrorsForValidConnectToGamePrimitiveInExecutionContext() {
    var primitiveAction = new PrimitiveConnectToGameAction {
      GameId = "game-1",
      AdbSerial = "emulator-5554"
    };

    var errors = PrimitiveActionValidationService.Validate(primitiveAction, PrimitiveActionSelectionContext.ExecutionConnect);

    errors.Should().BeEmpty();
  }

  [Fact]
  public void ValidateRejectsExecutionConnectContextForNonConnectPrimitive() {
    var primitiveAction = new PrimitiveTapAction {
      X = 10,
      Y = 20
    };

    var errors = PrimitiveActionValidationService.Validate(primitiveAction, PrimitiveActionSelectionContext.ExecutionConnect);

    errors.Should().ContainSingle(error => error.Contains("Execution connect context requires a connect-to-game primitive action"));
  }

  [Fact]
  public void ValidateReturnsNoErrorsForGoToHomeScreenPrimitive() {
    var primitiveAction = new PrimitiveGoToHomeScreenAction();

    var errors = PrimitiveActionValidationService.Validate(primitiveAction);

    errors.Should().BeEmpty();
  }

  [Fact]
  public void ValidateRejectsMissingRequiredFields() {
    var primitiveAction = new PrimitiveCommandAction();

    var errors = PrimitiveActionValidationService.Validate(primitiveAction);

    errors.Should().ContainSingle(error => error.Contains("Command primitive actions require commandId"));
  }

  [Fact]
  public void SupportedActionTypesIncludesCurrentCanonicalSet() {
    PrimitiveActionValidationService.SupportedActionTypes.Should().BeEquivalentTo(new[] {
      PrimitiveActionTypes.Tap,
      PrimitiveActionTypes.Swipe,
      PrimitiveActionTypes.Key,
      PrimitiveActionTypes.Command,
      PrimitiveActionTypes.ConnectToGame,
      PrimitiveActionTypes.WaitForImage,
      PrimitiveActionTypes.EnsureGameRunning,
      PrimitiveActionTypes.GoToHomeScreen,
      PrimitiveActionTypes.EnsureEmulatorRunning
    });
  }
}
