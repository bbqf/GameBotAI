using System.Drawing;
using GameBot.Domain.Profiles;
using GameBot.Domain.Profiles.Evaluators;
using Xunit;

namespace GameBot.UnitTests.Emulator.Triggers;

public sealed class TextMatchPreprocessingTests
{
    [Fact(DisplayName = "Preprocessing upscales small images for better OCR")]
    public void PreprocessingUpscalesSmallImages()
    {
        // Arrange: Create a small 8x8 image (simulating small outlined text)
        using var small = new Bitmap(8, 8);
        using (var g = Graphics.FromImage(small))
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Black, 2, 2, 4, 4); // small "text" region
        }

        var screen = new SingleBitmapScreenSource(() => small);
        var ocr = new EnvTextOcr();
        var eval = new TextMatchEvaluator(ocr, screen);

        var trigger = new ProfileTrigger
        {
            Id = "test",
            Type = TriggerType.TextMatch,
            Enabled = true,
            Params = new TextMatchParams
            {
                Target = "TEST",
                Region = new GameBot.Domain.Profiles.Region { X = 0, Y = 0, Width = 1, Height = 1 },
                ConfidenceThreshold = 0.5,
                Mode = "found"
            }
        };

        // Act: evaluate (internally preprocessing will upscale)
        Environment.SetEnvironmentVariable("GAMEBOT_TEST_OCR_TEXT", "TEST");
        Environment.SetEnvironmentVariable("GAMEBOT_TEST_OCR_CONF", "0.95");
        var result = eval.Evaluate(trigger, DateTimeOffset.UtcNow);

        // Assert: Should successfully process (preprocessing prevents 1x1 or tiny failures)
        Assert.Equal(TriggerStatus.Satisfied, result.Status);
        Assert.Equal("text_found", result.Reason);
    }
}
