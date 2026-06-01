using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Contracts.QueueTemplates;

namespace GameBot.Service.Endpoints;

internal static class QueueTemplatesEndpoints {
  public static IEndpointRouteBuilder MapQueueTemplateEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.QueueTemplates).WithTags("QueueTemplates");

    group.MapGet("", async (IQueueTemplateRepository repo) => {
      var list = await repo.ListAsync().ConfigureAwait(false);
      var resp = list.Select(BuildSummary).ToList();
      return Results.Ok(resp);
    }).WithName("ListQueueTemplates");

    group.MapGet("{id}", async (string id, IQueueTemplateRepository repo, ISequenceRepository sequences) => {
      var template = await repo.GetAsync(id).ConfigureAwait(false);
      if (template is null) return NotFound();
      return Results.Ok(await BuildDetailAsync(template, sequences).ConfigureAwait(false));
    }).WithName("GetQueueTemplate");

    group.MapPost("", async (SaveQueueTemplateRequest? req, IQueueTemplateRepository repo, ISequenceRepository sequences) => {
      var name = req?.Name?.Trim();
      var nameError = ValidateName(name);
      if (nameError is not null) return Error(400, "invalid_request", nameError);

      var existing = await repo.FindByNameAsync(name!).ConfigureAwait(false);
      if (existing is not null && !req!.Overwrite) {
        return Error(409, "template_exists",
          $"A template named '{existing.Name}' already exists.",
          "Resend with overwrite=true to replace.");
      }

      var target = existing ?? new QueueTemplate();
      target.Name = name!;
      target.Entries.Clear();
      foreach (var sid in req!.SequenceIds ?? Array.Empty<string>()) {
        target.Entries.Add(new QueueTemplateEntry { SequenceId = sid });
      }

      if (existing is null) {
        var created = await repo.CreateAsync(target).ConfigureAwait(false);
        var detail = await BuildDetailAsync(created, sequences).ConfigureAwait(false);
        return Results.Created($"{ApiRoutes.QueueTemplates}/{created.Id}", detail);
      }

      var saved = await repo.UpdateAsync(target).ConfigureAwait(false);
      return Results.Ok(await BuildDetailAsync(saved, sequences).ConfigureAwait(false));
    }).WithName("SaveQueueTemplate");

    group.MapDelete("{id}", async (string id, IQueueTemplateRepository repo) => {
      return await repo.DeleteAsync(id).ConfigureAwait(false)
        ? Results.NoContent()
        : NotFound();
    }).WithName("DeleteQueueTemplate");

    return app;
  }

  private static readonly System.Text.RegularExpressions.Regex NamePattern =
    new("^[A-Za-z0-9 _-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

  private static string? ValidateName(string? trimmedName) {
    if (string.IsNullOrWhiteSpace(trimmedName)) return "name is required";
    if (trimmedName.Length > 100) return "name must be 100 characters or fewer";
    if (!NamePattern.IsMatch(trimmedName)) {
      return "name may contain only letters, digits, spaces, hyphens, and underscores";
    }
    return null;
  }

  private static QueueTemplateSummaryResponse BuildSummary(QueueTemplate template) => new() {
    Id = template.Id,
    Name = template.Name,
    EntryCount = template.Entries.Count,
    CreatedAt = template.CreatedAt,
    UpdatedAt = template.UpdatedAt
  };

  private static async Task<QueueTemplateDetailResponse> BuildDetailAsync(QueueTemplate template, ISequenceRepository sequences) {
    var allSequences = await sequences.ListAsync().ConfigureAwait(false);
    var namesById = allSequences.ToDictionary(s => s.Id, s => s.Name, StringComparer.Ordinal);
    var detail = new QueueTemplateDetailResponse {
      Id = template.Id,
      Name = template.Name,
      EntryCount = template.Entries.Count,
      CreatedAt = template.CreatedAt,
      UpdatedAt = template.UpdatedAt
    };
    foreach (var entry in template.Entries) {
      var found = namesById.TryGetValue(entry.SequenceId, out var name);
      detail.Entries.Add(new QueueTemplateEntryResponse {
        SequenceId = entry.SequenceId,
        SequenceName = found ? name : null,
        Stale = !found
      });
    }
    return detail;
  }

  private static IResult NotFound() => Error(404, "not_found", "Queue template not found");

  private static IResult Error(int status, string code, string message, string? hint = null) =>
    Results.Json(new { error = new { code, message, hint } }, statusCode: status);
}
