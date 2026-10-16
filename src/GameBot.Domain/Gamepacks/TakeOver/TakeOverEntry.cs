namespace GameBot.Domain.Gamepacks;

/// <summary>One object that an install takes over from the user (feature 149).</summary>
/// <param name="Type">One of <see cref="GamepackObjectTypes"/>: sequence, command, or image.</param>
/// <param name="Name">The object name. For an image, the id.</param>
/// <param name="Identical">True when the canonical content of the user object equals the canonical content of the gamepack object.</param>
public sealed record TakeOverEntry(string Type, string Name, bool Identical);
