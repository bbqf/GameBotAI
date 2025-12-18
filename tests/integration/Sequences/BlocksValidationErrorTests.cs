using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences
{
    public class BlocksValidationErrorTests
    {
        private static WebApplicationFactory<Program> CreateFactory()
        {
            Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
            Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
            return new WebApplicationFactory<Program>();
        }

        private sealed class ErrorDto
        {
            public string message { get; set; } = string.Empty;
            public string[] errors { get; set; } = Array.Empty<string>();
        }

        [Fact]
        public async Task ElseStepsNotAllowedOutsideIfElse()
        {
            using var app = CreateFactory();
            var client = app.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

            var seq = new
            {
                name = "neg_else",
                blocks = new object[]
                {
                    new { type = "repeatCount", maxIterations = 1, elseSteps = new object[] { new { order = 1, commandId = "c1" } } }
                }
            };

            var resp = await client.PostAsJsonAsync("/api/sequences", seq);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ErrorDto>();
            Assert.NotNull(err);
            Assert.Contains(err!.errors, e => e.Contains("elseSteps", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task RepeatCountCadenceOutOfBounds()
        {
            using var app = CreateFactory();
            var client = app.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

            var seq = new
            {
                name = "neg_cadence_repeat",
                blocks = new object[]
                {
                    new { type = "repeatCount", maxIterations = 1, cadenceMs = 10, steps = new object[] { new { order = 1, commandId = "c1" } } }
                }
            };

            var resp = await client.PostAsJsonAsync("/api/sequences", seq);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ErrorDto>();
            Assert.NotNull(err);
            Assert.Contains(err!.errors, e => e.Contains("repeatCount cadenceMs", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task WhileMissingConditionAndLimits()
        {
            using var app = CreateFactory();
            var client = app.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

            var seq = new
            {
                name = "neg_while_missing",
                blocks = new object[]
                {
                    new { type = "while", steps = new object[] { new { order = 1, commandId = "c1" } } }
                }
            };

            var resp = await client.PostAsJsonAsync("/api/sequences", seq);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ErrorDto>();
            Assert.NotNull(err);
            Assert.Contains(err!.errors, e => e.Contains("while block requires 'condition'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(err!.errors, e => e.Contains("while block must set 'timeoutMs' or 'maxIterations'", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task RepeatUntilCadenceOutOfBoundsHigh()
        {
            using var app = CreateFactory();
            var client = app.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

            var seq = new
            {
                name = "neg_repeatuntil_cadence",
                blocks = new object[]
                {
                    new { type = "repeatUntil", timeoutMs = 1000, cadenceMs = 6001, condition = new { source = "trigger", targetId = "t", mode = "Present" }, steps = new object[] { new { order = 1, commandId = "c1" } } }
                }
            };

            var resp = await client.PostAsJsonAsync("/api/sequences", seq);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ErrorDto>();
            Assert.NotNull(err);
            Assert.Contains(err!.errors, e => e.Contains("repeatUntil cadenceMs out of bounds", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task RepeatCountNegativeMaxIterations()
        {
            using var app = CreateFactory();
            var client = app.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

            var seq = new
            {
                name = "neg_repeatcount_max",
                blocks = new object[]
                {
                    new { type = "repeatCount", maxIterations = -1, steps = new object[] { new { order = 1, commandId = "c1" } } }
                }
            };

            var resp = await client.PostAsJsonAsync("/api/sequences", seq);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ErrorDto>();
            Assert.NotNull(err);
            Assert.Contains(err!.errors, e => e.Contains("repeatCount requires non-negative 'maxIterations'", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task UnsupportedBlockTypeAndMissingType()
        {
            using var app = CreateFactory();
            var client = app.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

            var seq = new
            {
                name = "neg_block_type",
                blocks = new object[]
                {
                    new { type = "unknownType" },
                    new { steps = new object[] { new { order = 1, commandId = "c1" } } }
                }
            };

            var resp = await client.PostAsJsonAsync("/api/sequences", seq);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<ErrorDto>();
            Assert.NotNull(err);
            Assert.Contains(err!.errors, e => e.Contains("Unsupported block type", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(err!.errors, e => e.Contains("Block missing required 'type'", StringComparison.OrdinalIgnoreCase));
        }
    }
}
