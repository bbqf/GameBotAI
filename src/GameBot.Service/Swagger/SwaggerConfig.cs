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
using GameBot.Service.Endpoints;

namespace GameBot.Service.Swagger;

internal static class SwaggerConfig
{
  private static readonly string[] ActionsTags = ["Actions"];
  private static readonly string[] CommandsTags = ["Commands"];
  private static readonly string[] GamesTags = ["Games"];
  private static readonly string[] SequencesTags = ["Sequences"];
  private static readonly string[] SessionsTags = ["Sessions"];
  private static readonly string[] ConfigurationTags = ["Configuration"];
  private static readonly string[] TriggersTags = ["Triggers"];
  private static readonly string[] ImagesTags = ["Images"];
  private static readonly string[] MetricsTags = ["Metrics"];
  private static readonly string[] EmulatorsTags = ["Emulators"];

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
        if (tags.Count > 0)
        {
          return tags;
        }

        var derived = DeriveTags(api.RelativePath);
        if (derived.Length > 0)
        {
          return derived;
        }

        return Array.Empty<string>();
      });

      options.OperationFilter<SwaggerExamplesOperationFilter>();
    });

    return services;
  }

  private static string[] DeriveTags(string? relativePath)
  {
    if (string.IsNullOrWhiteSpace(relativePath))
    {
      return Array.Empty<string>();
    }

    var path = "/" + relativePath.TrimStart('/');

    if (path.StartsWith(ApiRoutes.Actions, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(ApiRoutes.ActionTypes, StringComparison.OrdinalIgnoreCase))
    {
      return ActionsTags;
    }

    if (path.StartsWith(ApiRoutes.Commands, StringComparison.OrdinalIgnoreCase))
    {
      return CommandsTags;
    }

    if (path.StartsWith(ApiRoutes.Games, StringComparison.OrdinalIgnoreCase))
    {
      return GamesTags;
    }

    if (path.StartsWith(ApiRoutes.Sequences, StringComparison.OrdinalIgnoreCase))
    {
      return SequencesTags;
    }

    if (path.StartsWith(ApiRoutes.Sessions, StringComparison.OrdinalIgnoreCase))
    {
      return SessionsTags;
    }

    if (path.StartsWith(ApiRoutes.ConfigLogging, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(ApiRoutes.Config, StringComparison.OrdinalIgnoreCase))
    {
      return ConfigurationTags;
    }

    if (path.StartsWith(ApiRoutes.Triggers, StringComparison.OrdinalIgnoreCase))
    {
      return TriggersTags;
    }

    if (path.StartsWith(ApiRoutes.Images, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(ApiRoutes.ImageDetect, StringComparison.OrdinalIgnoreCase))
    {
      return ImagesTags;
    }

    if (path.StartsWith(ApiRoutes.Metrics, StringComparison.OrdinalIgnoreCase))
    {
      return MetricsTags;
    }

    if (path.StartsWith(ApiRoutes.Adb, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(ApiRoutes.Ocr, StringComparison.OrdinalIgnoreCase))
    {
      return EmulatorsTags;
    }

    return Array.Empty<string>();
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

    // Apply by path so examples appear even if tags are missing or reordered
    ApplyActionTypesExamples(operation, path, method, context);
    ApplyActionExamples(operation, path, method, context);
    ApplyCommandExamples(operation, path, method, context);
    ApplySequenceExamples(operation, path, method, context);
    ApplySessionExamples(operation, path, method, context);
    ApplyConfigurationExamples(operation, path, method, context);
    ApplyTriggerExamples(operation, path, method, context);
    ApplyGameExamples(operation, path, method, context);
    ApplyImageExamples(operation, path, method, context);
  }

  private static void ApplyActionExamples(OpenApiOperation operation, string path, string method, OperationFilterContext context)
  {
    if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.Actions))
    {
      operation.Summary ??= "Create an action";
      SetRequestExample(operation, ActionCreateRequest(), context, typeof(GameBot.Service.Models.CreateActionRequest));
      SetResponseExample(operation, "201", ActionCreateResponse(), context, typeof(GameBot.Service.Models.ActionResponse));
    }
    else if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.Actions))
    {
      operation.Summary ??= "List actions";
      SetResponseExample(operation, "200", ActionListResponse(), context, typeof(IEnumerable<GameBot.Service.Models.ActionResponse>));
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Actions + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get an action";
      SetResponseExample(operation, "200", ActionCreateResponse(), context, typeof(GameBot.Service.Models.ActionResponse));
    }
    else if ((IsMethod(method, HttpMethods.Patch) || IsMethod(method, HttpMethods.Put)) && path.StartsWith(ApiRoutes.Actions + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Update an action";
      SetRequestExample(operation, ActionUpdateRequest(), context, typeof(ActionUpdateSchema));
      SetResponseExample(operation, "200", ActionCreateResponse(), context, typeof(GameBot.Service.Models.ActionResponse));
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains("/duplicate", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Duplicate an action";
      SetResponseExample(operation, "201", ActionCreateResponse(), context, typeof(GameBot.Service.Models.ActionResponse));
    }
  }

  private static void ApplyActionTypesExamples(OpenApiOperation operation, string path, string method, OperationFilterContext context)
  {
    if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.ActionTypes))
    {
      operation.Summary ??= "List available action types";
      SetResponseExample(operation, "200", ActionTypesResponse(), context, typeof(ActionTypeCatalogDto));
    }
  }

  private static void ApplyCommandExamples(OpenApiOperation operation, string path, string method, OperationFilterContext context)
  {
    if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.Commands))
    {
      operation.Summary ??= "Create a command";
      SetRequestExample(operation, CommandCreateRequest(), context, typeof(CommandCreateSchema));
      SetResponseExample(operation, "201", CommandCreateResponse(), context, typeof(GameBot.Service.Models.CommandResponse));
    }
    else if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.Commands))
    {
      operation.Summary ??= "List commands";
      SetResponseExample(operation, "200", CommandListResponse(), context, typeof(IEnumerable<GameBot.Service.Models.CommandResponse>));
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Commands + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get a command";
      SetResponseExample(operation, "200", CommandCreateResponse(), context, typeof(GameBot.Service.Models.CommandResponse));
    }
    else if (IsMethod(method, HttpMethods.Patch) && path.StartsWith(ApiRoutes.Commands + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Update a command";
      SetRequestExample(operation, CommandUpdateRequest(), context, typeof(GameBot.Service.Models.UpdateCommandRequest));
      SetResponseExample(operation, "200", CommandCreateResponse(), context, typeof(GameBot.Service.Models.CommandResponse));
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains("/force-execute", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Force execute a command";
      SetResponseExample(operation, "202", SessionInputsResponse(), context, typeof(SessionAcceptedSchema));
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains("/evaluate-and-execute", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Evaluate trigger then execute";
      SetResponseExample(operation, "202", CommandEvaluateResponse(), context, typeof(CommandEvaluateResponseSchema));
    }
  }

  private static void ApplySequenceExamples(OpenApiOperation operation, string path, string method, OperationFilterContext context)
  {
    if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.Sequences))
    {
      operation.Summary ??= "Create a sequence";
      SetRequestExample(operation, SequenceCreateRequest(), context, typeof(SequenceRequestSchema));
      SetResponseExample(operation, "201", SequenceCreateResponse(), context, typeof(SequenceResponseSchema));
    }
    else if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.Sequences))
    {
      operation.Summary ??= "List sequences";
      SetResponseExample(operation, "200", SequenceListResponse(), context, typeof(IEnumerable<SequenceResponseSchema>));
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Sequences + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get a sequence";
      SetResponseExample(operation, "200", SequenceCreateResponse(), context, typeof(SequenceResponseSchema));
    }
    else if (IsMethod(method, HttpMethods.Put) && path.StartsWith(ApiRoutes.Sequences + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Update a sequence";
      SetRequestExample(operation, SequenceCreateRequest(), context, typeof(SequenceRequestSchema));
      SetResponseExample(operation, "200", SequenceCreateResponse(), context, typeof(SequenceResponseSchema));
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains("/execute", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Execute a sequence";
      SetResponseExample(operation, "200", SequenceExecuteResponse(), context, typeof(GameBot.Domain.Services.SequenceExecutionResult));
    }
  }

  private static void ApplySessionExamples(OpenApiOperation operation, string path, string method, OperationFilterContext context)
  {
    if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.Sessions))
    {
      operation.Summary ??= "Create a session";
      SetRequestExample(operation, SessionCreateRequest(), context, typeof(GameBot.Service.Models.CreateSessionRequest));
      SetResponseExample(operation, "201", SessionCreateResponse(), context, typeof(GameBot.Service.Models.CreateSessionResponse));
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains(ApiRoutes.Sessions + "/{id}/inputs", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Send inputs to a session";
      SetRequestExample(operation, SessionInputsRequest(), context, typeof(GameBot.Service.Models.InputActionsRequest));
      SetResponseExample(operation, "202", SessionInputsResponse(), context, typeof(SessionAcceptedSchema));
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains(ApiRoutes.Sessions + "/{id}/execute-action", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Execute an action against a session";
      SetResponseExample(operation, "202", SessionInputsResponse(), context, typeof(SessionAcceptedSchema));
    }
    else if (IsMethod(method, HttpMethods.Get) && path.Contains(ApiRoutes.Sessions + "/{id}/snapshot", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Fetch a session snapshot";
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Sessions + "/" , StringComparison.OrdinalIgnoreCase) && path.EndsWith("/device" , StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get session device";
      SetResponseExample(operation, "200", SessionDeviceResponse(), context, typeof(SessionDeviceSchema));
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Sessions + "/" , StringComparison.OrdinalIgnoreCase) && path.EndsWith("/health" , StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get session health";
      SetResponseExample(operation, "200", SessionHealthResponse(), context, typeof(SessionHealthSchema));
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Sessions + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get a session";
      SetResponseExample(operation, "200", SessionGetResponse(), context, typeof(SessionDetailSchema));
    }
    else if (IsMethod(method, HttpMethods.Delete) && path.StartsWith(ApiRoutes.Sessions + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Stop a session";
      SetResponseExample(operation, "202", SessionStopResponse(), context, typeof(SessionStopSchema));
    }
  }

  private static void ApplyConfigurationExamples(OpenApiOperation operation, string path, string method, OperationFilterContext context)
  {
    if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.Config))
    {
      operation.Summary ??= "Get configuration snapshot";
      SetResponseExample(operation, "200", ConfigurationSnapshotResponse(), context, typeof(ConfigurationSnapshotSchema));
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains(ApiRoutes.Config + "/refresh", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Refresh configuration snapshot";
      SetResponseExample(operation, "200", ConfigurationSnapshotResponse(), context, typeof(ConfigurationSnapshotSchema));
    }
    else if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.ConfigLogging))
    {
      operation.Summary ??= "Get runtime logging policy";
      SetResponseExample(operation, "200", LoggingPolicyResponse(), context, typeof(LoggingPolicySchema));
    }
    else if (IsMethod(method, HttpMethods.Put) && path.Contains(ApiRoutes.ConfigLogging + "/components/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Update logging component policy";
      SetRequestExample(operation, LoggingPolicyUpdateRequest(), context, typeof(GameBot.Service.Models.Logging.LoggingComponentPatchDto));
      SetResponseExample(operation, "200", LoggingPolicyResponse(), context, typeof(LoggingPolicySchema));
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains(ApiRoutes.ConfigLogging + "/reset", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Reset logging policy";
      SetRequestExample(operation, LoggingPolicyResetRequest(), context, typeof(LoggingPolicyResetRequestSchema));
      SetResponseExample(operation, "200", LoggingPolicyResponse(), context, typeof(LoggingPolicySchema));
    }
  }

  private static void ApplyTriggerExamples(OpenApiOperation operation, string path, string method, OperationFilterContext context)
  {
    if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.Triggers))
    {
      operation.Summary ??= "Create a trigger";
      SetRequestExample(operation, TriggerCreateRequest(), context, typeof(TriggerAuthoringSchema));
      SetResponseExample(operation, "201", TriggerCreateResponse(), context, typeof(TriggerResponseSchema));
    }
    else if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.Triggers))
    {
      operation.Summary ??= "List triggers";
      SetResponseExample(operation, "200", TriggerListResponse(), context, typeof(IEnumerable<TriggerResponseSchema>));
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Triggers + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get a trigger";
      SetResponseExample(operation, "200", TriggerCreateResponse(), context, typeof(TriggerResponseSchema));
    }
    else if (IsMethod(method, HttpMethods.Put) && path.StartsWith(ApiRoutes.Triggers + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Update a trigger";
      SetRequestExample(operation, TriggerCreateRequest(), context, typeof(TriggerAuthoringSchema));
      SetResponseExample(operation, "200", TriggerCreateResponse(), context, typeof(TriggerResponseSchema));
    }
    else if (IsMethod(method, HttpMethods.Post) && path.Contains(ApiRoutes.Triggers + "/{id}/test", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Evaluate a trigger";
      SetResponseExample(operation, "200", TriggerTestResponse(), context, typeof(TriggerTestResponseSchema));
    }
    else if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.ImageDetect))
    {
      operation.Summary ??= "Detect reference image matches";
      SetRequestExample(operation, ImageDetectRequest(), context, typeof(GameBot.Service.Endpoints.Dto.DetectRequest));
      SetResponseExample(operation, "200", ImageDetectResponse(), context, typeof(GameBot.Service.Endpoints.Dto.DetectResponse));
    }
  }

  private static void ApplyGameExamples(OpenApiOperation operation, string path, string method, OperationFilterContext context)
  {
    if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.Games))
    {
      operation.Summary ??= "Create a game";
      SetRequestExample(operation, GameCreateRequest(), context, typeof(GameCreateSchema));
      SetResponseExample(operation, "201", GameCreateResponse(), context, typeof(GameResponseSchema));
    }
    else if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.Games))
    {
      operation.Summary ??= "List games";
      SetResponseExample(operation, "200", GameListResponse(), context, typeof(IEnumerable<GameResponseSchema>));
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Games + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get a game";
      SetResponseExample(operation, "200", GameCreateResponse(), context, typeof(GameResponseSchema));
    }
    else if (IsMethod(method, HttpMethods.Put) && path.StartsWith(ApiRoutes.Games + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Update a game";
      SetRequestExample(operation, GameUpdateRequest(), context, typeof(GameCreateSchema));
      SetResponseExample(operation, "200", GameCreateResponse(), context, typeof(GameResponseSchema));
    }
  }

  private static void ApplyImageExamples(OpenApiOperation operation, string path, string method, OperationFilterContext context)
  {
    if (IsMethod(method, HttpMethods.Get) && IsPath(path, ApiRoutes.Images))
    {
      operation.Summary ??= "List image references";
      SetResponseExample(operation, "200", ImageReferenceListResponse(), context, typeof(IEnumerable<ImageReferenceListItemSchema>));
    }
    else if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.Images))
    {
      operation.Summary ??= "Upload an image reference";
      SetRequestExample(operation, ImageUploadRequest(), context, typeof(GameBot.Service.Endpoints.ImageReferencesEndpoints.UploadImageRequest));
      SetResponseExample(operation, "201", ImageUploadResponse(), context, typeof(ImageUploadResponseSchema));
    }
    else if (IsMethod(method, HttpMethods.Get) && path.StartsWith(ApiRoutes.Images + "/", StringComparison.OrdinalIgnoreCase))
    {
      operation.Summary ??= "Get an image reference";
      SetResponseExample(operation, "200", ImageReferenceResponse(), context, typeof(ImageReferenceSchema));
    }
    else if (IsMethod(method, HttpMethods.Post) && IsPath(path, ApiRoutes.ImageDetect))
    {
      operation.Summary ??= "Detect reference image matches";
      SetRequestExample(operation, ImageDetectRequest(), context, typeof(GameBot.Service.Endpoints.Dto.DetectRequest));
      SetResponseExample(operation, "200", ImageDetectResponse(), context, typeof(GameBot.Service.Endpoints.Dto.DetectResponse));
    }
  }

  private static string NormalizePath(string? relativePath)
  {
    if (string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
    var trimmed = relativePath.TrimStart('/');
    return "/" + trimmed;
  }

  private static bool IsPath(string actual, string expected)
  {
    static string Normalize(string path) => path.EndsWith('/') ? path.TrimEnd('/') : path;
    return string.Equals(Normalize(actual), Normalize(expected), StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsMethod(string method, string expected) => string.Equals(method, expected, StringComparison.OrdinalIgnoreCase);

  private static void SetRequestExample(OpenApiOperation operation, IOpenApiAny example, OperationFilterContext context, Type? schemaType = null)
  {
    operation.RequestBody ??= new OpenApiRequestBody { Content = new Dictionary<string, OpenApiMediaType>() };
    if (!operation.RequestBody.Content.TryGetValue("application/json", out var media))
    {
      media = new OpenApiMediaType();
      operation.RequestBody.Content["application/json"] = media;
    }
    media.Example = example;
    media.Schema ??= GenerateSchema(context, schemaType);
  }

  private static void SetResponseExample(OpenApiOperation operation, string statusCode, IOpenApiAny example, OperationFilterContext context, Type? schemaType = null)
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
    media.Schema ??= GenerateSchema(context, schemaType);
  }

  private static OpenApiSchema GenerateSchema(OperationFilterContext context, Type? schemaType)
  {
    var type = schemaType ?? typeof(object);
    return context.SchemaGenerator.GenerateSchema(type, context.SchemaRepository);
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

  private static OpenApiObject ActionTypesResponse() => new OpenApiObject
  {
    ["version"] = new OpenApiString("v1"),
    ["items"] = new OpenApiArray
    {
      new OpenApiObject
      {
        ["key"] = new OpenApiString("tap"),
        ["displayName"] = new OpenApiString("Tap"),
        ["description"] = new OpenApiString("Tap at coordinates"),
        ["attributeDefinitions"] = new OpenApiArray
        {
          new OpenApiObject
          {
            ["key"] = new OpenApiString("x"),
            ["label"] = new OpenApiString("X"),
            ["dataType"] = new OpenApiString("number"),
            ["required"] = new OpenApiBoolean(true),
            ["constraints"] = new OpenApiObject { ["min"] = new OpenApiDouble(0), ["max"] = new OpenApiDouble(5000) },
            ["helpText"] = new OpenApiString("X coordinate")
          },
          new OpenApiObject
          {
            ["key"] = new OpenApiString("y"),
            ["label"] = new OpenApiString("Y"),
            ["dataType"] = new OpenApiString("number"),
            ["required"] = new OpenApiBoolean(true),
            ["constraints"] = new OpenApiObject { ["min"] = new OpenApiDouble(0), ["max"] = new OpenApiDouble(5000) },
            ["helpText"] = new OpenApiString("Y coordinate")
          }
        }
      },
      new OpenApiObject
      {
        ["key"] = new OpenApiString("key"),
        ["displayName"] = new OpenApiString("Key Event"),
        ["description"] = new OpenApiString("Send an Android key event"),
        ["attributeDefinitions"] = new OpenApiArray
        {
          new OpenApiObject
          {
            ["key"] = new OpenApiString("key"),
            ["label"] = new OpenApiString("Key name"),
            ["dataType"] = new OpenApiString("string"),
            ["required"] = new OpenApiBoolean(true),
            ["constraints"] = new OpenApiObject
            {
              ["allowedValues"] = new OpenApiArray
              {
                new OpenApiString("HOME"), new OpenApiString("BACK"), new OpenApiString("ESCAPE")
              }
            },
            ["helpText"] = new OpenApiString("Symbolic key name")
          },
          new OpenApiObject
          {
            ["key"] = new OpenApiString("keyCode"),
            ["label"] = new OpenApiString("Key code"),
            ["dataType"] = new OpenApiString("number"),
            ["required"] = new OpenApiBoolean(false),
            ["constraints"] = new OpenApiObject { ["min"] = new OpenApiDouble(0), ["max"] = new OpenApiDouble(300) },
            ["helpText"] = new OpenApiString("Optional numeric Android key code")
          }
        }
      }
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

  private static OpenApiObject ActionUpdateRequest() => new OpenApiObject
  {
    ["name"] = new OpenApiString("Collect coins v2"),
    ["gameId"] = new OpenApiString("game-123"),
    ["steps"] = new OpenApiArray
    {
      new OpenApiObject
      {
        ["type"] = new OpenApiString("swipe"),
        ["args"] = new OpenApiObject { ["x1"] = new OpenApiDouble(10), ["y1"] = new OpenApiDouble(20), ["x2"] = new OpenApiDouble(200), ["y2"] = new OpenApiDouble(220) },
        ["delayMs"] = new OpenApiInteger(0),
        ["durationMs"] = new OpenApiInteger(120)
      }
    },
    ["checkpoints"] = new OpenApiArray { new OpenApiString("after-start") }
  };

  private static OpenApiObject CommandCreateRequest() => new OpenApiObject
  {
    ["name"] = new OpenApiString("Warmup"),
    ["actions"] = new OpenApiArray { new OpenApiString("action-abc"), new OpenApiString("action-def") }
  };

  private static OpenApiObject CommandUpdateRequest() => new OpenApiObject
  {
    ["name"] = new OpenApiString("Warmup v2"),
    ["triggerId"] = new OpenApiString("trigger-1"),
    ["steps"] = new OpenApiArray
    {
      new OpenApiObject { ["type"] = new OpenApiString("Action"), ["targetId"] = new OpenApiString("action-abc"), ["order"] = new OpenApiInteger(0) },
      new OpenApiObject { ["type"] = new OpenApiString("Command"), ["targetId"] = new OpenApiString("cmd-next"), ["order"] = new OpenApiInteger(1) }
    }
  };

  private static OpenApiObject CommandCreateResponse() => new OpenApiObject
  {
    ["id"] = new OpenApiString("command-123"),
    ["name"] = new OpenApiString("Warmup"),
    ["triggerId"] = new OpenApiString("trigger-1"),
    ["steps"] = new OpenApiArray
    {
      new OpenApiObject { ["type"] = new OpenApiString("Action"), ["targetId"] = new OpenApiString("action-abc"), ["order"] = new OpenApiInteger(0) }
    }
  };

  private static OpenApiArray CommandListResponse() => new OpenApiArray { CommandCreateResponse() };

  private static OpenApiObject CommandEvaluateResponse() => new OpenApiObject
  {
    ["accepted"] = new OpenApiBoolean(true),
    ["triggerStatus"] = new OpenApiString("Satisfied"),
    ["message"] = new OpenApiString("Trigger satisfied; executed")
  };

  private static OpenApiObject SequenceExecuteResponse() => new OpenApiObject
  {
    ["sequenceId"] = new OpenApiString("sequence-xyz"),
    ["status"] = new OpenApiString("Succeeded"),
    ["startedAt"] = new OpenApiString("2025-12-28T12:00:00Z"),
    ["endedAt"] = new OpenApiString("2025-12-28T12:00:02Z"),
    ["steps"] = new OpenApiArray
    {
      new OpenApiObject
      {
        ["order"] = new OpenApiInteger(0),
        ["commandId"] = new OpenApiString("command-start"),
        ["status"] = new OpenApiString("Succeeded"),
        ["attempts"] = new OpenApiInteger(1),
        ["durationMs"] = new OpenApiInteger(120),
        ["appliedDelayMs"] = new OpenApiInteger(0)
      }
    }
  };

  private static OpenApiObject SessionGetResponse() => new OpenApiObject
  {
    ["id"] = new OpenApiString("session-123"),
    ["status"] = new OpenApiString("RUNNING"),
    ["uptime"] = new OpenApiInteger(42),
    ["health"] = new OpenApiString("HEALTHY"),
    ["gameId"] = new OpenApiString("game-123")
  };

  private static OpenApiObject SessionDeviceResponse() => new OpenApiObject
  {
    ["id"] = new OpenApiString("session-123"),
    ["deviceSerial"] = new OpenApiString("emulator-5554"),
    ["mode"] = new OpenApiString("ADB")
  };

  private static OpenApiObject SessionHealthResponse() => new OpenApiObject
  {
    ["id"] = new OpenApiString("session-123"),
    ["mode"] = new OpenApiString("ADB"),
    ["deviceSerial"] = new OpenApiString("emulator-5554"),
    ["adb"] = new OpenApiObject
    {
      ["ok"] = new OpenApiBoolean(true),
      ["stdout"] = new OpenApiString("device"),
      ["stderr"] = new OpenApiString(string.Empty)
    }
  };

  private static OpenApiObject SessionStopResponse() => new OpenApiObject
  {
    ["status"] = new OpenApiString("stopping")
  };

  private static OpenApiObject GameCreateRequest() => new OpenApiObject
  {
    ["name"] = new OpenApiString("Awesome Game"),
    ["metadata"] = new OpenApiObject { ["genre"] = new OpenApiString("arcade"), ["version"] = new OpenApiString("1.0.0") }
  };

  private static OpenApiObject GameCreateResponse() => new OpenApiObject
  {
    ["id"] = new OpenApiString("game-123"),
    ["name"] = new OpenApiString("Awesome Game"),
    ["metadata"] = new OpenApiObject { ["genre"] = new OpenApiString("arcade"), ["version"] = new OpenApiString("1.0.0") }
  };

  private static OpenApiArray GameListResponse() => new OpenApiArray { GameCreateResponse() };

  private static OpenApiObject GameUpdateRequest() => new OpenApiObject
  {
    ["name"] = new OpenApiString("Awesome Game v2"),
    ["description"] = new OpenApiString("Updated description")
  };

  private static OpenApiObject ImageUploadRequest() => new OpenApiObject
  {
    ["id"] = new OpenApiString("start-screen"),
    ["data"] = new OpenApiString("iVBORw0KGgoAAAANSUhEUgAAAAUA")
  };

  private static OpenApiObject ImageUploadResponse() => new OpenApiObject
  {
    ["id"] = new OpenApiString("start-screen"),
    ["overwrite"] = new OpenApiBoolean(false)
  };

  private static OpenApiArray ImageReferenceListResponse() => new OpenApiArray
  {
    new OpenApiObject { ["id"] = new OpenApiString("start-screen") },
    new OpenApiObject { ["id"] = new OpenApiString("pause-screen") }
  };

  private static OpenApiObject ImageReferenceResponse() => new OpenApiObject
  {
    ["id"] = new OpenApiString("start-screen")
  };
}

internal sealed class ActionUpdateSchema
{
  public string? Name { get; set; }
  public string? GameId { get; set; }
  public ICollection<GameBot.Service.Models.InputActionDto>? Steps { get; set; }
  public ICollection<string>? Checkpoints { get; set; }
}

internal sealed class SequenceRequestSchema
{
  public string? Name { get; set; }
  public ICollection<string>? Steps { get; set; }
}

internal sealed class SequenceResponseSchema
{
  public required string Id { get; set; }
  public required string Name { get; set; }
  public ICollection<string> Steps { get; set; } = new List<string>();
}

internal sealed class SessionAcceptedSchema
{
  public int Accepted { get; set; }
}

internal sealed class SessionDetailSchema
{
  public required string Id { get; set; }
  public required string Status { get; set; }
  public long Uptime { get; set; }
  public string? Health { get; set; }
  public string? GameId { get; set; }
}

internal sealed class SessionDeviceSchema
{
  public required string Id { get; set; }
  public string? DeviceSerial { get; set; }
  public string? Mode { get; set; }
}

internal sealed class SessionHealthSchema
{
  public required string Id { get; set; }
  public string? Mode { get; set; }
  public string? DeviceSerial { get; set; }
  public SessionHealthAdbSchema? Adb { get; set; }
}

internal sealed class SessionHealthAdbSchema
{
  public bool Ok { get; set; }
  public string? Stdout { get; set; }
  public string? Stderr { get; set; }
}

internal sealed class SessionStopSchema
{
  public string? Status { get; set; }
}

internal sealed class ConfigurationSnapshotSchema
{
  public DateTimeOffset GeneratedAtUtc { get; set; }
  public string? ServiceVersion { get; set; }
  public int RefreshCount { get; set; }
  public IDictionary<string, ConfigParameterSchema>? Parameters { get; set; }
}

internal sealed class ConfigParameterSchema
{
  public string? Name { get; set; }
  public string? Source { get; set; }
  public string? Value { get; set; }
  public bool IsSecret { get; set; }
}

internal sealed class LoggingPolicySchema
{
  public DateTimeOffset UpdatedAtUtc { get; set; }
  public ICollection<LoggingComponentSchema> Components { get; set; } = new List<LoggingComponentSchema>();
}

internal sealed class LoggingComponentSchema
{
  public string? Name { get; set; }
  public string? Level { get; set; }
  public bool Enabled { get; set; }
  public string? Notes { get; set; }
}

internal sealed class LoggingPolicyResetRequestSchema
{
  public string? Reason { get; set; }
}

internal sealed class TriggerAuthoringSchema
{
  public string? Name { get; set; }
  public TriggerCriteriaSchema? Criteria { get; set; }
  public ICollection<string>? Actions { get; set; }
  public ICollection<string>? Commands { get; set; }
  public string? Sequence { get; set; }
}

internal sealed class TriggerCriteriaSchema
{
  public string? Type { get; set; }
  public string? ReferenceImageId { get; set; }
  public RegionSchema? Region { get; set; }
  public double? SimilarityThreshold { get; set; }
}

internal sealed class RegionSchema
{
  public double X { get; set; }
  public double Y { get; set; }
  public double Width { get; set; }
  public double Height { get; set; }
}

internal sealed class TriggerResponseSchema
{
  public required string Id { get; set; }
  public string? Name { get; set; }
  public TriggerCriteriaSchema? Criteria { get; set; }
  public ICollection<string>? Actions { get; set; }
  public ICollection<string>? Commands { get; set; }
  public string? Sequence { get; set; }
}

internal sealed class TriggerTestResponseSchema
{
  public string? Status { get; set; }
  public DateTimeOffset EvaluatedAt { get; set; }
  public string? Reason { get; set; }
}

internal sealed class CommandCreateSchema
{
  public required string Name { get; set; }
  public string? TriggerId { get; set; }
  public ICollection<string>? Actions { get; set; }
  public ICollection<GameBot.Service.Models.CommandStepDto>? Steps { get; set; }
}

internal sealed class CommandEvaluateResponseSchema
{
  public bool Accepted { get; set; }
  public string? TriggerStatus { get; set; }
  public string? Message { get; set; }
}

internal sealed class GameCreateSchema
{
  public required string Name { get; set; }
  public object? Metadata { get; set; }
  public string? Description { get; set; }
}

internal sealed class GameResponseSchema
{
  public required string Id { get; set; }
  public required string Name { get; set; }
  public object? Metadata { get; set; }
  public string? Description { get; set; }
}

internal sealed class ImageUploadResponseSchema
{
  public string? Id { get; set; }
  public bool Overwrite { get; set; }
}

internal sealed class ImageReferenceListItemSchema
{
  public string? Id { get; set; }
}

internal sealed class ImageReferenceSchema
{
  public string? Id { get; set; }
  public bool? Fallback { get; set; }
}
