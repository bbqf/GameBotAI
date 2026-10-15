using GameBot.Domain.Gamepacks;
using GameBot.Service.Endpoints.Dto;

namespace GameBot.Service.Endpoints;

/// <summary>The API version route (feature 144). The handler is a named method.</summary>
internal static class VersionEndpoints {
  public static IEndpointRouteBuilder MapVersionEndpoints(this IEndpointRouteBuilder app) {
    app.MapGet(ApiRoutes.Version, GetApiVersion)
      .Produces<ApiVersionResponse>(StatusCodes.Status200OK)
      .WithName("GetApiVersion")
      .WithTags("Version")
      .WithSummary("Read the API version of the platform")
      .WithDescription(
        "Gives the API major, minor, and patch version and the schema format version. "
        + "A gamepack requiredApiMajor must equal the major version.");

    return app;
  }

  private static IResult GetApiVersion() =>
    Results.Ok(new ApiVersionResponse {
      Major = ApiVersion.Major,
      Minor = ApiVersion.Minor,
      Patch = ApiVersion.Patch,
      SchemaFormat = ApiVersion.SchemaFormat
    });
}
