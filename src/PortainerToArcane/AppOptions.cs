namespace PortainerToArcane;

public sealed record AppOptions(
    string? BackupPath,
    string? OutputDirectory,
    string PortainerUrl,
    string ApiKeyEnvironmentVariable,
    string SessionPath,
    bool NonInteractive,
    bool SelectAll,
    bool AnalyzeOnly)
{
    public static AppOptions Parse(string[] args)
    {
        string? backup = null;
        string? output = null;
        var portainerUrl = "http://localhost:9000";
        var apiKeyEnvironmentVariable = "PORTAINER_API_KEY";
        var sessionPath = Path.Combine(Environment.CurrentDirectory, ".portainer-to-arcane", "session.json");
        var nonInteractive = false;
        var selectAll = false;
        var analyzeOnly = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--backup":
                    backup = GetValue(args, ref index);
                    break;
                case "--output":
                    output = GetValue(args, ref index);
                    break;
                case "--portainer-url":
                    portainerUrl = GetValue(args, ref index);
                    break;
                case "--api-key-env":
                    apiKeyEnvironmentVariable = GetValue(args, ref index);
                    break;
                case "--session":
                    sessionPath = GetValue(args, ref index);
                    break;
                case "--non-interactive":
                    nonInteractive = true;
                    break;
                case "--all":
                    selectAll = true;
                    break;
                case "--analyze-only":
                    analyzeOnly = true;
                    break;
                case "--help":
                case "-h":
                    throw new HelpRequestedException();
                default:
                    throw new ArgumentException($"Option inconnue : {args[index]}");
            }
        }

        return new(
            backup,
            output,
            portainerUrl,
            apiKeyEnvironmentVariable,
            sessionPath,
            nonInteractive,
            selectAll,
            analyzeOnly);
    }

    private static string GetValue(string[] args, ref int index)
    {
        index++;
        if (index >= args.Length)
        {
            throw new ArgumentException("Valeur manquante pour la dernière option.");
        }
        return args[index];
    }
}

public sealed class HelpRequestedException : Exception;
