using GameBot.Domain.Gamepacks.Signing;
using GameBot.Service.Endpoints.Dto;

namespace GameBot.Service.Endpoints;

/// <summary>
/// The routes for the gamepack signature policy and the trusted keys (feature 146). The routes use the same API token rule as
/// all other routes. The handlers are named methods, because the taint analyzers cost more on large lambdas.
/// </summary>
internal static class GamepackSignatureEndpoints {
  public static IEndpointRouteBuilder MapGamepackSignatureEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.GamepackSignature).WithTags("Gamepacks");

    group.MapGet("policy", GetPolicy)
      .Produces<GamepackSignaturePolicyDto>(StatusCodes.Status200OK)
      .WithName("GetGamepackSignaturePolicy")
      .WithSummary("Read the gamepack signature policy")
      .WithDescription("Gives the policy: required, optional, or off. The default is off.");

    group.MapPut("policy", SetPolicy)
      .Accepts<GamepackSignaturePolicyDto>("application/json")
      .Produces<GamepackSignaturePolicyDto>(StatusCodes.Status200OK)
      .ProducesProblem(StatusCodes.Status400BadRequest)
      .WithName("SetGamepackSignaturePolicy")
      .WithSummary("Set the gamepack signature policy")
      .WithDescription("Sets the policy to required, optional, or off. The next install uses it. A restart is not needed.");

    group.MapGet("keys", ListKeys)
      .Produces<IReadOnlyList<GamepackSignatureKeyDto>>(StatusCodes.Status200OK)
      .WithName("ListGamepackSignatureKeys")
      .WithSummary("List the trusted signing keys")
      .WithDescription("Gives the label and the fingerprint of each trusted key. The response has no public key text.");

    group.MapPost("keys", AddKey)
      .Accepts<GamepackSignatureKeyRequest>("application/json")
      .Produces<GamepackSignatureKeyDto>(StatusCodes.Status201Created)
      .ProducesProblem(StatusCodes.Status400BadRequest)
      .ProducesProblem(StatusCodes.Status409Conflict)
      .WithName("AddGamepackSignatureKey")
      .WithSummary("Add a trusted signing key")
      .WithDescription(
        "Adds an ECDSA P-256 public key (base64 SubjectPublicKeyInfo) and a label of 1 to 100 characters. "
        + "The fingerprint is the SHA-256 of the key bytes in lower case hex. A key that is not valid is not saved.");

    group.MapDelete("keys/{fingerprint}", RemoveKey)
      .Produces(StatusCodes.Status204NoContent)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .WithName("RemoveGamepackSignatureKey")
      .WithSummary("Remove a trusted signing key")
      .WithDescription("Removes the key with this fingerprint. The next install does not trust it.");

    return app;
  }

  private static IResult GetPolicy(IGamepackSignatureSettingsStore store) =>
    Results.Ok(new GamepackSignaturePolicyDto { Policy = store.GetPolicy().ToWire() });

  private static IResult SetPolicy(GamepackSignaturePolicyDto? request, IGamepackSignatureSettingsStore store) {
    if (!SignaturePolicyNames.TryParse(request?.Policy, out var policy)) {
      return Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "The policy must be required, optional, or off.",
        extensions: new Dictionary<string, object?> { ["result"] = "invalid_policy" });
    }

    store.SetPolicy(policy);
    return Results.Ok(new GamepackSignaturePolicyDto { Policy = policy.ToWire() });
  }

  private static IResult ListKeys(IGamepackSignatureSettingsStore store) =>
    Results.Ok(store.Load().Keys.Select(GamepackSignatureKeyDto.From).ToList());

  private static IResult AddKey(GamepackSignatureKeyRequest? request, IGamepackSignatureSettingsStore store) {
    var outcome = store.AddKey(request?.Label, request?.PublicKey);
    return outcome.Status switch {
      AddKeyStatus.Added => Results.Json(GamepackSignatureKeyDto.From(outcome.Key!), statusCode: StatusCodes.Status201Created),
      AddKeyStatus.Duplicate => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "A key with this fingerprint is on the list.",
        extensions: new Dictionary<string, object?> { ["result"] = "duplicate_key" }),
      AddKeyStatus.InvalidLabel => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "The label must have 1 to 100 characters.",
        extensions: new Dictionary<string, object?> { ["result"] = "invalid_label" }),
      _ => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "The public key must be a base64 SubjectPublicKeyInfo of an ECDSA P-256 key.",
        extensions: new Dictionary<string, object?> { ["result"] = "invalid_key" })
    };
  }

  private static IResult RemoveKey(string fingerprint, IGamepackSignatureSettingsStore store) =>
    store.RemoveKey(fingerprint)
      ? Results.NoContent()
      : Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "No key has this fingerprint.",
        extensions: new Dictionary<string, object?> { ["result"] = "not_found" });
}
