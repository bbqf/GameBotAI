namespace GameBot.Service.Endpoints.Dto;

/// <summary>The API version of the platform (feature 144).</summary>
internal sealed record ApiVersionResponse {
  /// <summary>The API major version.</summary>
  public required int Major { get; init; }

  /// <summary>The API minor version.</summary>
  public required int Minor { get; init; }

  /// <summary>The API patch version.</summary>
  public required int Patch { get; init; }

  /// <summary>The format version of the sequence and command schema.</summary>
  public required int SchemaFormat { get; init; }
}
