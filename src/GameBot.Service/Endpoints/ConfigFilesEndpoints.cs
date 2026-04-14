using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameBot.Domain.Services.Logging;
using GameBot.Service.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GameBot.Service.Endpoints;

internal static class ConfigFilesEndpoints
{
    private static readonly FrozenSet<string> AllowedFiles = new[]
    {
        "execution-log-policy.json",
        "logging-policy.json"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static IEndpointRouteBuilder MapConfigFilesEndpoints(this IEndpointRouteBuilder app, string storageRoot)
    {
        var configDir = Path.Combine(storageRoot, "config");

        app.MapGet(ApiRoutes.ConfigFiles, () =>
        {
            return Results.Ok(new { files = AllowedFiles.ToArray() });
        })
        .WithName("ListConfigFiles")
        .WithTags("Configuration");

        app.MapGet(ApiRoutes.ConfigFiles + "/{name}", (string name) =>
        {
            var safeName = ValidateFileName(name);
            if (safeName is null)
                return Results.NotFound(new { error = new { code = "file_not_found", message = $"Unknown config file '{name}'." } });

            var filePath = Path.Combine(configDir, safeName);
            return Results.Ok(BuildSnapshot(safeName, filePath));
        })
        .WithName("GetConfigFile")
        .WithTags("Configuration");

        app.MapPut(ApiRoutes.ConfigFiles + "/{name}", async (string name, ConfigFileUpdateRequest req, HttpContext ctx) =>
        {
            var safeName = ValidateFileName(name);
            if (safeName is null)
                return Results.NotFound(new { error = new { code = "file_not_found", message = $"Unknown config file '{name}'." } });

            if (req.Updates is null || req.Updates.Count == 0)
                return Results.BadRequest(new { error = new { code = "invalid_payload", message = "At least one update is required." } });

            var filePath = Path.Combine(configDir, safeName);
            Directory.CreateDirectory(configDir);

            JsonNode root;
            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath, ctx.RequestAborted).ConfigureAwait(false);
                root = JsonNode.Parse(json) ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            ApplyUpdates(root, req.Updates);

            var tmpPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, root.ToJsonString(WriteOptions), ctx.RequestAborted).ConfigureAwait(false);
            File.Move(tmpPath, filePath, overwrite: true);

            // Apply changes to the running instance
            await ApplyRuntimeChangesAsync(safeName, ctx).ConfigureAwait(false);

            return Results.Ok(BuildSnapshot(safeName, filePath));
        })
        .WithName("UpdateConfigFile")
        .WithTags("Configuration");

        return app;
    }

    private static async Task ApplyRuntimeChangesAsync(string fileName, HttpContext ctx)
    {
        switch (fileName)
        {
            case "logging-policy.json":
                var loggingSvc = ctx.RequestServices.GetService<IRuntimeLoggingPolicyService>();
                if (loggingSvc is not null)
                    await loggingSvc.ReloadFromDiskAsync(ctx.RequestAborted).ConfigureAwait(false);
                break;
            // execution-log-policy.json is read from disk on every access — no reload needed.
        }
    }

    private static string? ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var safeName = Path.GetFileName(name);
        return AllowedFiles.Contains(safeName) ? safeName : null;
    }

    private static ConfigFileSnapshot BuildSnapshot(string fileName, string filePath)
    {
        var parameters = new Dictionary<string, ConfigurationParameter>();
        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);
            var node = JsonNode.Parse(json);
            foreach (var (key, value) in FlattenNode(node, ""))
            {
                parameters[key] = new ConfigurationParameter
                {
                    Name = key,
                    Source = "File",
                    Value = value,
                    IsSecret = false
                };
            }
        }
        return new ConfigFileSnapshot { FileName = fileName, Parameters = parameters };
    }

    private static List<KeyValuePair<string, object?>> FlattenNode(JsonNode? node, string prefix)
    {
        var result = new List<KeyValuePair<string, object?>>();
        if (node is JsonObject obj)
        {
            foreach (var prop in obj)
            {
                var key = string.IsNullOrEmpty(prefix) ? prop.Key : $"{prefix}:{prop.Key}";
                result.AddRange(FlattenNode(prop.Value, key));
            }
        }
        else if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var key = $"{prefix}:{i}";
                result.AddRange(FlattenNode(arr[i], key));
            }
        }
        else if (node is JsonValue val)
        {
            result.Add(new(prefix, GetScalarValue(val)));
        }
        else
        {
            result.Add(new(prefix, null));
        }
        return result;
    }

    private static object? GetScalarValue(JsonValue val)
    {
        if (val.TryGetValue<bool>(out var b)) return b;
        if (val.TryGetValue<int>(out var i)) return i;
        if (val.TryGetValue<long>(out var l)) return l;
        if (val.TryGetValue<double>(out var d)) return d;
        if (val.TryGetValue<string>(out var s)) return s;
        return val.ToString();
    }

    private static void ApplyUpdates(JsonNode root, Dictionary<string, string?> updates)
    {
        foreach (var (key, newValue) in updates)
        {
            var segments = key.Split(':');
            SetNestedValue(root, segments, 0, newValue);
        }
    }

    private static void SetNestedValue(JsonNode current, string[] segments, int index, string? newValue)
    {
        if (index >= segments.Length) return;
        var segment = segments[index];

        if (index == segments.Length - 1)
        {
            // Leaf node — set value
            if (current is JsonObject obj)
            {
                obj[segment] = CoerceValue(newValue, obj[segment]);
            }
            else if (current is JsonArray arr && int.TryParse(segment, out var arrIdx) && arrIdx >= 0 && arrIdx < arr.Count)
            {
                arr[arrIdx] = CoerceValue(newValue, arr[arrIdx]);
            }
        }
        else
        {
            // Navigate deeper
            JsonNode? child = null;
            if (current is JsonObject obj)
                child = obj[segment];
            else if (current is JsonArray arr && int.TryParse(segment, out var arrIdx) && arrIdx >= 0 && arrIdx < arr.Count)
                child = arr[arrIdx];

            if (child is not null)
                SetNestedValue(child, segments, index + 1, newValue);
        }
    }

    private static JsonValue? CoerceValue(string? newValue, JsonNode? existing)
    {
        if (newValue is null) return null;

        // Preserve original type when possible
        if (existing is JsonValue val)
        {
            if (val.TryGetValue<bool>(out _) && bool.TryParse(newValue, out var b))
                return JsonValue.Create(b);
            if (val.TryGetValue<int>(out _) && int.TryParse(newValue, out var i))
                return JsonValue.Create(i);
            if (val.TryGetValue<long>(out _) && long.TryParse(newValue, out var l))
                return JsonValue.Create(l);
            if (val.TryGetValue<double>(out _) && double.TryParse(newValue, out var d))
                return JsonValue.Create(d);
        }

        // Default to string
        return JsonValue.Create(newValue);
    }
}
