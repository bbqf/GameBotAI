using System;
using System.Collections.Generic;
using System.Linq;
using GameBot.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

internal static class SwaggerConfig
{
  private static readonly string[] DefaultTags = ["Default"];

  public static IServiceCollection AddSwaggerDocs(this IServiceCollection services)
  {
    services.AddSwaggerGen(options =>
    {
      options.SwaggerDoc("v1", new OpenApiInfo
      {
        Title = "GameBot API",
        Version = "v1",
        Description = "Canonical GameBot endpoints under /api",
      });

      options.TagActionsBy(api =>
      {
        var tags = api.ActionDescriptor.EndpointMetadata.OfType<TagsAttribute>()
                      .SelectMany(t => t.Tags)
                      .ToList();
        return tags.Count > 0 ? tags : DefaultTags;
      });

      options.OperationFilter<SwaggerExamplesOperationFilter>();
    });

    return services;
  }

  public static IApplicationBuilder UseSwaggerDocs(this IApplicationBuilder app)
  {
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
      options.SwaggerEndpoint("/swagger/v1/swagger.json", "GameBot API v1");
      options.DisplayRequestDuration();
      options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    });
    return app;
  }
}

internal sealed class SwaggerExamplesOperationFilter : IOperationFilter
{
  public void Apply(OpenApiOperation operation, OperationFilterContext context)
  {
    var path = NormalizePath(context.ApiDescription.RelativePath);
    if (!path.StartsWith(ApiRoutes.Base, StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    var method = context.ApiDescription.HttpMethod ?? string.Empty;
    var tag = operation.Tags?.FirstOrDefault()?.Name ?? string.Empty;

    switch (tag)
    {
      case "Actions":
        ApplyActionExamples(operation, path, method);
        break;
      case "Sequences":
        ApplySequenceExamples(operation, path, method);
        break;
      case "Sessions":
        ApplySessionExamples(operation, path, method);
        break;
      case "Configuration":
        ApplyConfigurationExamples(operation, path, method);
        break;
      case "Triggers":
      case "Images":
        ApplyTriggerExamples(operation, path, method);
        break;
      default:
        break;
    }
  }

  private static void ApplyActionExamples(OpenApiOperation operation, string path, string method)
  {
    if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.Actions))
    {
      operation.Summary ??= "Create an action";
      SetRequestExample(operation, ActionCreateRequest());
      SetResponseExample(operation, "201", ActionCreateResponse());
    }
    else if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.Actions))
    {
      operation.Summary ??= "List actions";
      SetResponseExample(operation, "200", ActionListResponse());
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Actions + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get an action";
      SetResponseExample(operation, "200", ActionCreateResponse());
    }
  }

  private static void ApplySequenceExamples(OpenApiOperation operation, string path, string method)
  {
    if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.Sequences))
    {
      operation.Summary ??= "Create a sequence";
      SetRequestExample(operation, SequenceCreateRequest());
      SetResponseExample(operation, "201", SequenceCreateResponse());
    }
    else if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.Sequences))
    {
      operation.Summary ??= "List sequences";
      SetResponseExample(operation, "200", SequenceListResponse());
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Sequences + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get a sequence";
      SetResponseExample(operation, "200", SequenceCreateResponse());
    }
  }

  private static void ApplySessionExamples(OpenApiOperation operation, string path, string method)
  {
    if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.Sessions))
    {
      operation.Summary ??= "Create a session";
      SetRequestExample(operation, SessionCreateRequest());
      SetResponseExample(operation, "201", SessionCreateResponse());
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains(ApiRoutes.Sessions + "/{id}/inputs", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Send inputs to a session";
      SetRequestExample(operation, SessionInputsRequest());
      SetResponseExample(operation, "202", SessionInputsResponse());
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains(ApiRoutes.Sessions + "/{id}/execute-action", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Execute an action against a session";
      SetResponseExample(operation, "202", SessionInputsResponse());
    }
    else if (IsMethod(method, HttpMethods.Get) && path.Contains(ApiRoutes.Sessions + "/{id}/snapshot", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Fetch a session snapshot";
    }
  }

  private static void ApplyConfigurationExamples(OpenApiOperation operation, string path, string method)
  {
    if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.Config))
    {
      operation.Summary ??= "Get configuration snapshot";
      SetResponseExample(operation, "200", ConfigurationSnapshotResponse());
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains(ApiRoutes.Config + "/refresh", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Refresh configuration snapshot";
      SetResponseExample(operation, "200", ConfigurationSnapshotResponse());
    }
    else if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.ConfigLogging))
    {
      operation.Summary ??= "Get runtime logging policy";
      SetResponseExample(operation, "200", LoggingPolicyResponse());
    }
    else if (IsMethod(method, HttpMethods.Put) && path.Contains(ApiRoutes.ConfigLogging + "/components/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Update logging component policy";
      SetRequestExample(operation, LoggingPolicyUpdateRequest());
      SetResponseExample(operation, "200", LoggingPolicyResponse());
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains(ApiRoutes.ConfigLogging + "/reset", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Reset logging policy";
      SetRequestExample(operation, LoggingPolicyResetRequest());
      SetResponseExample(operation, "200", LoggingPolicyResponse());
    }
  }

  private static void ApplyTriggerExamples(OpenApiOperation operation, string path, string method)
  {
    if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.Triggers))
    {
      operation.Summary ??= "Create a trigger";
      SetRequestExample(operation, TriggerCreateRequest());
      SetResponseExample(operation, "201", TriggerCreateResponse());
    }
    else if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.Triggers))
    {
      operation.Summary ??= "List triggers";
      SetResponseExample(operation, "200", TriggerListResponse());
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Triggers + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get a trigger";
      SetResponseExample(operation, "200", TriggerCreateResponse());
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains(ApiRoutes.Triggers + "/{id}/test", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Evaluate a trigger";
      SetResponseExample(operation, "200", TriggerTestResponse());
    }
    else if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.ImageDetect))
    {
      operation.Summary ??= "Detect reference image matches";
      SetRequestExample(operation, ImageDetectRequest());
      SetResponseExample(operation, "200", ImageDetectResponse());
    }
  }

  private static string NormalizePath(string? relativePath)
  {
    if (string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
    var trimmed = relativePath.TrimStart('/');
    return "/" + trimmed;
  }

  private static bool IsPath(string actual, string expected) => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

  private static bool IsMethod(string method, string expected) => string.Equals(method, expected, StringComparison.OrdinalIgnoreCase);

  private static void SetRequestExample(OpenApiOperation operation, IOpenApiAny example)
  {
    operation.RequestBody ??= new OpenApiRequestBody { Content = new Dictionary<string, OpenApiMediaType>() };
    if (!operation.RequestBody.Content.TryGetValue("application/json", out var media))
    {
      media = new OpenApiMediaType();
      operation.RequestBody.Content["application/json"] = media;
    }
    media.Example = example;
  }

  private static void SetResponseExample(OpenApiOperation operation, string statusCode, IOpenApiAny example)
  {
    if (!operation.Responses.TryGetValue(statusCode, out var response))
    {
      response = new OpenApiResponse { Description = statusCode.Length > 0 && statusCode[0] == '2' ? "Success" : "Response" };
      operation.Responses[statusCode] = response;
    }

    response.Content ??= new Dictionary<string, OpenApiMediaType>();
    if (!response.Content.TryGetValue("application/json", out var media))
    {
      media = new OpenApiMediaType();
      response.Content["application/json"] = media;
    }
    media.Example = example;
  }

  private static OpenApiObject ActionCreateRequest() => new OpenApiObject
  {
    ["name"] = new OpenApiString("Collect coins"),
    ["gameId"] = new OpenApiString("game-123"),
    ["steps"] = new OpenApiArray
    {
      new OpenApiObject
      {
        ["type"] = new OpenApiString("tap"),
        ["args"] = new OpenApiObject
        {
          ["x"] = new OpenApiDouble(120),
          ["y"] = new OpenApiDouble(240)
        },
        ["delayMs"] = new OpenApiInteger(0),
        ["durationMs"] = new OpenApiInteger(0)
      }
    },
    ["checkpoints"] = new OpenApiArray { new OpenApiString("after-start"), new OpenApiString("before-exit") }
  };

  private static OpenApiObject ActionCreateResponse() => new OpenApiObject
  {
    ["id"] = new OpenApiString("action-abc"),
    ["name"] = new OpenApiString("Collect coins"),
    ["gameId"] = new OpenApiString("game-123"),
    ["steps"] = new OpenApiArray
    {
      new OpenApiObject
      {
        ["type"] = new OpenApiString("tap"),
        ["args"] = new OpenApiObject
        {
          ["x"] = new OpenApiDouble(120),
          ["y"] = new OpenApiDouble(240)
        },
        ["delayMs"] = new OpenApiInteger(0),
        ["durationMs"] = new OpenApiInteger(0)
      }
    },
    ["checkpoints"] = new OpenApiArray { new OpenApiString("after-start"), new OpenApiString("before-exit") }
  };

  private static OpenApiArray ActionListResponse() => new OpenApiArray
  {
    ActionCreateResponse()
  };

  private static OpenApiObject SequenceCreateRequest() => new OpenApiObject
  {
    ["name"] = new OpenApiString("Morning routine"),
    ["steps"] = new OpenApiArray
    {
      new OpenApiString("command-warmup"),
      new OpenApiString("command-start")
    }
  };

  private static OpenApiObject SequenceCreateResponse() => new OpenApiObject
  {
    ["id"] = new OpenApiString("sequence-xyz"),
    ["name"] = new OpenApiString("Morning routine"),
    ["steps"] = new OpenApiArray
    {
      new OpenApiString("command-warmup"),
      new OpenApiString("command-start")
    }
  };

  private static OpenApiArray SequenceListResponse() => new OpenApiArray
  {
    SequenceCreateResponse()
  };

  private static OpenApiObject SessionCreateRequest() => new OpenApiObject
  {
    ["gameId"] = new OpenApiString("game-123"),
    ["adbSerial"] = new OpenApiString("emulator-5554")
  };

  private static OpenApiObject SessionCreateResponse() => new OpenApiObject
  {
    ["id"] = new OpenApiString("session-123"),
    ["status"] = new OpenApiString("PENDING"),
    ["gameId"] = new OpenApiString("game-123")
  };

  private static OpenApiObject SessionInputsRequest() => new OpenApiObject
  {
    ["actions"] = new OpenApiArray
    {
      new OpenApiObject
      {
        ["type"] = new OpenApiString("tap"),
        ["args"] = new OpenApiObject
        {
          ["x"] = new OpenApiDouble(50),
          ["y"] = new OpenApiDouble(75)
        },
        ["delayMs"] = new OpenApiInteger(0),
        ["durationMs"] = new OpenApiInteger(0)
      }
    }
  };

  private static OpenApiObject SessionInputsResponse() => new OpenApiObject
  {
    ["accepted"] = new OpenApiInteger(1)
  };

  private static OpenApiObject ConfigurationSnapshotResponse() => new OpenApiObject
  {
    ["generatedAtUtc"] = new OpenApiString("2025-12-28T12:00:00Z"),
    ["serviceVersion"] = new OpenApiString("1.0.0"),
    ["refreshCount"] = new OpenApiInteger(1),
    ["parameters"] = new OpenApiObject
    {
      ["GAMEBOT_DATA_DIR"] = new OpenApiObject { ["name"] = new OpenApiString("GAMEBOT_DATA_DIR"), ["source"] = new OpenApiString("Default"), ["value"] = new OpenApiString("C:/data"), ["isSecret"] = new OpenApiBoolean(false) },
      ["Service__Auth__Token"] = new OpenApiObject { ["name"] = new OpenApiString("Service__Auth__Token"), ["source"] = new OpenApiString("File"), ["value"] = new OpenApiString("***"), ["isSecret"] = new OpenApiBoolean(true) }
    }
  };

  private static OpenApiObject LoggingPolicyResponse() => new OpenApiObject
  {
    ["updatedAtUtc"] = new OpenApiString("2025-12-28T12:00:00Z"),
    ["components"] = new OpenApiArray
    {
      new OpenApiObject
      {
        ["name"] = new OpenApiString("GameBot.Service"),
        ["level"] = new OpenApiString("Warning"),
        ["enabled"] = new OpenApiBoolean(true),
        ["notes"] = new OpenApiString("default policy")
      }
    }
  };

  private static OpenApiObject LoggingPolicyUpdateRequest() => new OpenApiObject
  {
    ["level"] = new OpenApiString("Information"),
    ["enabled"] = new OpenApiBoolean(true),
    ["notes"] = new OpenApiString("Temporary override for debugging")
  };

  private static OpenApiObject LoggingPolicyResetRequest() => new OpenApiObject
  {
    ["reason"] = new OpenApiString("Revert to defaults")
  };

  private static OpenApiObject TriggerCreateRequest() => new OpenApiObject
  {
    ["name"] = new OpenApiString("Start screen ready"),
    ["criteria"] = new OpenApiObject
    {
      ["type"] = new OpenApiString("image-match"),
      ["referenceImageId"] = new OpenApiString("start-screen"),
      ["region"] = new OpenApiObject { ["x"] = new OpenApiDouble(0.1), ["y"] = new OpenApiDouble(0.1), ["width"] = new OpenApiDouble(0.5), ["height"] = new OpenApiDouble(0.5) },
      ["similarityThreshold"] = new OpenApiDouble(0.9)
    },
    ["actions"] = new OpenApiArray { new OpenApiString("action-abc") }
  };

  private static OpenApiObject TriggerCreateResponse() => new OpenApiObject
  {
    ["id"] = new OpenApiString("trigger-123"),
    ["name"] = new OpenApiString("Start screen ready"),
    ["criteria"] = new OpenApiObject
    {
      ["type"] = new OpenApiString("image-match"),
      ["referenceImageId"] = new OpenApiString("start-screen"),
      ["region"] = new OpenApiObject { ["x"] = new OpenApiDouble(0.1), ["y"] = new OpenApiDouble(0.1), ["width"] = new OpenApiDouble(0.5), ["height"] = new OpenApiDouble(0.5) },
      ["similarityThreshold"] = new OpenApiDouble(0.9)
    },
    ["actions"] = new OpenApiArray { new OpenApiString("action-abc") },
    ["commands"] = new OpenApiArray(),
    ["sequence"] = new OpenApiString("sequence-xyz")
  };

  private static OpenApiArray TriggerListResponse() => new OpenApiArray
  {
    TriggerCreateResponse()
  };

  private static OpenApiObject TriggerTestResponse() => new OpenApiObject
  {
    ["status"] = new OpenApiString("Satisfied"),
    ["evaluatedAt"] = new OpenApiString("2025-12-28T12:00:00Z"),
    ["reason"] = new OpenApiString("Image match exceeded threshold")
  };

  private static OpenApiObject ImageDetectRequest() => new OpenApiObject
  {
    ["referenceImageId"] = new OpenApiString("start-screen"),
    ["threshold"] = new OpenApiDouble(0.85),
    ["maxResults"] = new OpenApiInteger(3),
    ["overlap"] = new OpenApiDouble(0.1)
  };

  private static OpenApiObject ImageDetectResponse() => new OpenApiObject
  {
    ["limitsHit"] = new OpenApiBoolean(false),
    ["matches"] = new OpenApiArray
    {
      new OpenApiObject
      {
        ["confidence"] = new OpenApiDouble(0.93),
        ["bbox"] = new OpenApiObject
        {
          ["x"] = new OpenApiDouble(0.15),
          ["y"] = new OpenApiDouble(0.22),
          ["width"] = new OpenApiDouble(0.3),
          ["height"] = new OpenApiDouble(0.18)
        }
      }
    }
  };
}
