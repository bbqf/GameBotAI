using System.Text.RegularExpressions;

namespace GameBot.Domain.Triggers.Evaluators;

public static class ReferenceImageIdValidator
{
    private static readonly Regex IdRegex = new("^[A-Za-z0-9_-]{1,128}$", RegexOptions.Compiled);

    public static bool IsValid(string? id) => !string.IsNullOrWhiteSpace(id) && IdRegex.IsMatch(id);
}