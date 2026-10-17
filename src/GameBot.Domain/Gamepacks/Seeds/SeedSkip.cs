namespace GameBot.Domain.Gamepacks.Seeds;

/// <summary>A seed template that the install did not create (feature 150).</summary>
/// <param name="Name">The template name from the gamepack file.</param>
/// <param name="Reason">One of <see cref="SeedSkipReasons"/>.</param>
public sealed record SeedSkip(string Name, string Reason);
