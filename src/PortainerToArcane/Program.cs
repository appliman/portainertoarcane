using PortainerToArcane;
using Spectre.Console;

try
{
    var options = AppOptions.Parse(args);
    return await new MigrationApp().RunAsync(options);
}
catch (HelpRequestedException)
{
    AnsiConsole.WriteLine(
        "portainer-to-arcane --backup <archive.tar.gz> [--output <dir>] " +
        "[--portainer-url <url>] [--api-key-env <nom>] [--all] [--analyze-only] [--non-interactive]");
    return 0;
}
catch (OperationCanceledException)
{
    AnsiConsole.MarkupLine("[yellow]Opération annulée.[/]");
    return 130;
}
catch (Exception exception)
{
    AnsiConsole.MarkupLine($"[red]Erreur : {Markup.Escape(exception.Message)}[/]");
    return 1;
}
