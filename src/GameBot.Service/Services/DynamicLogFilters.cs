using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services;

internal static class DynamicLogFilters
{
    private static volatile LogLevel _httpMinLevel = LogLevel.Warning;

    public static LogLevel HttpMinLevel
    {
        get => _httpMinLevel;
        set => _httpMinLevel = value;
    }

    private static readonly string[] HttpCategories = new[]
    {
        "System.Net.Http",
        "Microsoft.AspNetCore.HttpLogging",
        "Microsoft.AspNetCore.Http.Result",
        "Microsoft.AspNetCore.Mvc.Infrastructure",
        "Microsoft.AspNetCore.Hosting.Diagnostics",
        "Microsoft.AspNetCore.Routing"
    };

    public static bool IsHttpCategory(string category)
    {
        foreach (var p in HttpCategories)
        {
            if (category.StartsWith(p, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
