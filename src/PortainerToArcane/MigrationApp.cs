using Spectre.Console;

namespace PortainerToArcane;

public sealed class MigrationApp
{
    private readonly BackupReader _backupReader = new();
    private readonly ComposeAnalyzer _analyzer = new();
    private readonly ArcaneExporter _exporter = new();
    private readonly SessionStore _sessionStore = new();

    public async Task<int> RunAsync(AppOptions options, CancellationToken cancellationToken = default)
    {
        var session = await _sessionStore.LoadAsync(options.SessionPath, cancellationToken);
        ShowHeader(session.CompletedSteps.Count > 0);

        var backupPath = ResolveBackupPath(options, session);
        var outputDirectory = ResolveOutputDirectory(options, session, backupPath);
        session.BackupPath = backupPath;
        session.OutputDirectory = outputDirectory;
        session.PortainerUrl = options.PortainerUrl;
        session.ApiKeyEnvironmentVariable = options.ApiKeyEnvironmentVariable;
        await CompleteStepAsync(options.SessionPath, session, "configuration", cancellationToken);

        BackupSnapshot snapshot = null!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Lecture sécurisée du backup Portainer...", async _ =>
            {
                snapshot = await _backupReader.ReadAsync(backupPath, cancellationToken);
            });
        ShowBackupSummary(snapshot);
        await CompleteStepAsync(options.SessionPath, session, "backup-analyzed", cancellationToken);

        await ShowLivePortainerStatusAsync(options, cancellationToken);

        var assessments = snapshot.Stacks.Select(_analyzer.Analyze).ToArray();
        var selected = SelectStacks(options, snapshot, assessments);
        session.SelectedStackIds = selected.Select(static item => item.Stack.Id).ToHashSet();
        ShowPreflight(selected);
        await CompleteStepAsync(options.SessionPath, session, "preflight", cancellationToken);

        if (options.AnalyzeOnly)
        {
            AnsiConsole.MarkupLine("[green]Analyse terminée.[/] Aucun fichier de projet n'a été exporté.");
            return 0;
        }

        if (!options.NonInteractive &&
            !AnsiConsole.Confirm(
                $"Exporter [bold]{selected.Count}[/] projets vers [yellow]{Markup.Escape(outputDirectory)}[/] ?",
                defaultValue: false))
        {
            AnsiConsole.MarkupLine("[yellow]Export annulé. La session peut être reprise plus tard.[/]");
            return 2;
        }

        MigrationManifest manifest = null!;
        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn())
            .StartAsync(async context =>
            {
                var task = context.AddTask("Export des projets Arcane", maxValue: 1);
                manifest = await _exporter.ExportAsync(snapshot, selected, outputDirectory, cancellationToken);
                task.Value = 1;
            });
        await CompleteStepAsync(options.SessionPath, session, "exported", cancellationToken);
        ShowCompletion(manifest, outputDirectory);
        return 0;
    }

    private static string ResolveBackupPath(AppOptions options, MigrationSession session)
    {
        var candidate = options.BackupPath ?? session.BackupPath;
        if (options.NonInteractive)
        {
            return candidate ?? throw new ArgumentException("--backup est obligatoire en mode non interactif.");
        }
        return AnsiConsole.Ask("Chemin du backup Portainer :", candidate ?? string.Empty);
    }

    private static string ResolveOutputDirectory(AppOptions options, MigrationSession session, string backupPath)
    {
        var defaultOutput = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(backupPath))!,
            "arcane-projects");
        var candidate = options.OutputDirectory ?? session.OutputDirectory ?? defaultOutput;
        return options.NonInteractive
            ? candidate
            : AnsiConsole.Ask("Dossier de sortie Arcane :", candidate);
    }

    private static IReadOnlyList<StackAssessment> SelectStacks(
        AppOptions options,
        BackupSnapshot snapshot,
        IReadOnlyList<StackAssessment> assessments)
    {
        if (options.NonInteractive || options.SelectAll)
        {
            return assessments;
        }

        var scope = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Quelle portée migrer ?")
                .AddChoices("Toutes les stacks", "Un endpoint", "Sélection manuelle"));
        if (scope == "Toutes les stacks")
        {
            return assessments;
        }
        if (scope == "Un endpoint")
        {
            var endpoints = snapshot.Endpoints.ToDictionary(
                endpoint => $"{endpoint.Id} - {endpoint.Name}",
                endpoint => endpoint.Id);
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Endpoint :")
                    .AddChoices(endpoints.Keys));
            return assessments.Where(item => item.Stack.EndpointId == endpoints[choice]).ToArray();
        }

        var labels = assessments.ToDictionary(
            item => $"[{item.Stack.EndpointId}] {item.Stack.Name} - {RiskText(item.Risk)}",
            item => item);
        var choices = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Stacks à exporter :")
                .PageSize(20)
                .Required()
                .InstructionsText("[grey](Espace pour sélectionner, Entrée pour valider)[/]")
                .AddChoices(labels.Keys));
        return choices.Select(choice => labels[choice]).ToArray();
    }

    private static void ShowHeader(bool resumed)
    {
        AnsiConsole.Write(new FigletText("Portainer > Arcane").Color(Color.Aqua));
        AnsiConsole.MarkupLine("[grey]Migration guidée, réversible et sans arrêt automatique.[/]");
        if (resumed)
        {
            AnsiConsole.MarkupLine("[blue]Session précédente détectée : reprise activée.[/]");
        }
        AnsiConsole.WriteLine();
    }

    private static void ShowBackupSummary(BackupSnapshot snapshot)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Backup");
        table.AddColumn("Valeur");
        table.AddRow("Archive", Markup.Escape(snapshot.ArchivePath));
        table.AddRow("Endpoints", FormatNumber(snapshot.Endpoints.Count));
        table.AddRow("Stacks", FormatNumber(snapshot.Stacks.Count));
        table.AddRow("Templates", FormatNumber(snapshot.CustomTemplateCount));
        table.AddRow("Entrées sensibles", FormatNumber(snapshot.SensitiveEntries.Count));
        AnsiConsole.Write(table);
    }

    private static void ShowPreflight(IReadOnlyList<StackAssessment> selected)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Endpoint");
        table.AddColumn("Stack");
        table.AddColumn("Risque");
        table.AddColumn("Contrôles");
        foreach (var item in selected)
        {
            table.AddRow(
                FormatNumber(item.Stack.EndpointId),
                Markup.Escape(item.Stack.Name),
                RiskMarkup(item.Risk),
                Markup.Escape(string.Join(", ", item.Findings.Select(static finding => finding.Message))));
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(
            $"Préflight : [green]{selected.Count(static item => item.Risk == MigrationRisk.Easy)} faciles[/], " +
            $"[yellow]{selected.Count(static item => item.Risk == MigrationRisk.Moderate)} modérées[/], " +
            $"[red]{selected.Count(static item => item.Risk == MigrationRisk.High)} élevées[/].");
    }

    private async Task ShowLivePortainerStatusAsync(AppOptions options, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            AnsiConsole.MarkupLine(
                $"[grey]API live ignorée : variable {Markup.Escape(options.ApiKeyEnvironmentVariable)} absente.[/]");
            return;
        }

        try
        {
            using var client = new PortainerClient(options.PortainerUrl, apiKey);
            var version = await client.GetVersionAsync(cancellationToken);
            var endpoints = await client.GetEndpointsAsync(cancellationToken);
            AnsiConsole.MarkupLine(
                $"[green]API Portainer connectée[/] - version {Markup.Escape(version ?? "inconnue")}, {endpoints.Count} endpoints.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            AnsiConsole.MarkupLine($"[yellow]API live indisponible : {Markup.Escape(exception.Message)}[/]");
        }
    }

    private static void ShowCompletion(MigrationManifest manifest, string outputDirectory)
    {
        AnsiConsole.Write(new Rule("[green]Export terminé[/]"));
        AnsiConsole.MarkupLine(
            $"[bold]{manifest.Projects.Count}[/] projets écrits dans [yellow]{Markup.Escape(Path.GetFullPath(outputDirectory))}[/].");
        AnsiConsole.MarkupLine(
            "Copier chaque dossier endpoint vers le PROJECTS_DIRECTORY de l'environnement Arcane correspondant.");
        AnsiConsole.MarkupLine(
            "[red]Ne pas déployer Portainer et Arcane simultanément sur une même stack.[/]");
    }

    private static string RiskMarkup(MigrationRisk risk) => risk switch
    {
        MigrationRisk.Easy => "[green]Facile[/]",
        MigrationRisk.Moderate => "[yellow]Modéré[/]",
        MigrationRisk.High => "[red]Élevé[/]",
        _ => "[grey]Inconnu[/]"
    };

    private static string RiskText(MigrationRisk risk) => risk switch
    {
        MigrationRisk.Easy => "Facile",
        MigrationRisk.Moderate => "Modéré",
        MigrationRisk.High => "Élevé",
        _ => "Inconnu"
    };

    private static string FormatNumber(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task CompleteStepAsync(
        string sessionPath,
        MigrationSession session,
        string step,
        CancellationToken cancellationToken)
    {
        session.CompletedSteps.Add(step);
        await _sessionStore.SaveAsync(sessionPath, session, cancellationToken);
    }
}
