namespace GameBot.Service;

internal static class ApiRoutes
{
    internal const string Base = "/api";
    internal const string Actions = Base + "/actions";
    internal const string Commands = Base + "/commands";
    internal const string Sequences = Base + "/sequences";
    internal const string Sessions = Base + "/sessions";
    internal const string Games = Base + "/games";
    internal const string ActionTypes = Base + "/action-types";
    internal const string Config = Base + "/config";
    internal const string ConfigLogging = Config + "/logging";
    internal const string Triggers = Base + "/triggers";
    internal const string Images = Base + "/images";
    internal const string ImageDetect = Images + "/detect";
    internal const string Metrics = Base + "/metrics";
    internal const string Adb = Base + "/adb";
    internal const string Ocr = Base + "/ocr";
}
