using System.Globalization;

namespace GameBot.TestAssets.TesseractStub;

internal static class Program
{
    private const int StdoutLength = 9000;

    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Expected at least input and output arguments.");
            return 1;
        }

        var inputPath = args[0];
        var outputBasePath = args[1];
        var language = ReadValue(args, "-l") ?? "eng";
        var psm = ReadValue(args, "--psm") ?? "6";
        var oem = ReadValue(args, "--oem") ?? "1";

        try
        {
            // Echo basic telemetry to stdout/stderr to exercise capture paths.
            var repeated = new string('S', StdoutLength);
            Console.Out.Write(repeated);
            Console.Error.WriteLine($"lang={language};psm={psm};oem={oem};input={Path.GetFileName(inputPath)}");

            var outputText = $"STUB_{language}_{psm}_{oem}_{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}";
            var textPath = outputBasePath + ".txt";
            File.WriteAllText(textPath, outputText);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("stub_failed:" + ex);
            return 2;
        }
    }

    private static string? ReadValue(string[] args, string key)
    {
        var idx = Array.IndexOf(args, key);
        if (idx >= 0 && idx + 1 < args.Length)
        {
            return args[idx + 1];
        }
        return null;
    }
}
