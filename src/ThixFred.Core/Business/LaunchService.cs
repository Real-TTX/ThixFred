using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Core.Platform;

namespace ThixFred.Core.Business;

/// <summary>
/// Carries out what a result does (Enter, Umschalt+Enter) and learns from it (<see cref="UsageService"/>). What only the UI
/// can do comes back as <see cref="LaunchOutcome"/> (a command of ThixFred, a message, a new search); the clipboard is given
/// to the business layer as a delegate (<see cref="Clipboard"/>). Errors become a message the launcher shows.
/// </summary>
public static class LaunchService
{
    /// <summary>Puts text into the clipboard (set by the app: Avalonia's clipboard).</summary>
    public static Func<string, Task>? Clipboard { get; set; }

    /// <summary>Runs <paramref name="action"/> of <paramref name="result"/>, chosen for <paramref name="query"/>.</summary>
    public static async Task<LaunchOutcome> ExecuteAsync(SearchResult result, LaunchAction action, string query)
    {
        try
        {
            var outcome = await RunAsync(action);
            if (outcome.Kind != LaunchOutcomeKind.Message || !outcome.IsError)
            {
                UsageService.Record(result.Id, FuzzyMatcher.Prepare(query.Trim()));
            }

            return outcome;
        }
        catch (BusinessException exception)
        {
            return LaunchOutcome.Show(exception.Message, isError: true);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Launch of {action.Kind} failed: {exception.Message}");
            return LaunchOutcome.Show($"Das ließ sich nicht öffnen: {exception.Message}", isError: true);
        }
    }

    public static async Task CopyAsync(string text)
    {
        if (Clipboard is null)
        {
            throw new BusinessException("Die Zwischenablage ist nicht verfügbar.");
        }

        await Clipboard(text);
    }

    private static async Task<LaunchOutcome> RunAsync(LaunchAction action)
    {
        switch (action.Kind)
        {
            case LaunchActionKind.Open:
                Shell.Open(action.Target);
                return LaunchOutcome.Done;

            case LaunchActionKind.Reveal:
                Shell.Reveal(action.Target);
                return LaunchOutcome.Done;

            case LaunchActionKind.OpenAsAdministrator:
                try
                {
                    Shell.OpenAsAdministrator(action.Target);
                }
                catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
                {
                    return LaunchOutcome.Show("Abgebrochen: keine Administratorrechte erteilt.");
                }

                return LaunchOutcome.Done;

            case LaunchActionKind.Copy:
                await CopyAsync(action.Target);
                return LaunchOutcome.Done;

            case LaunchActionKind.Terminal:
                TerminalService.Run(action.Target, asAdministrator: action.Argument == "admin");
                return LaunchOutcome.Done;

            case LaunchActionKind.IntegratedTerminal:
                if (action.Target.Trim().Length == 0)
                {
                    throw new BusinessException("Bitte einen Befehl nach „>“ eingeben, z. B. >ping heise.de");
                }

                return new LaunchOutcome(LaunchOutcomeKind.Terminal, Query: action.Target.Trim());

            case LaunchActionKind.StoreInstall:
                var listing = (await StoreService.LoadAsync()).FirstOrDefault(listing => listing.Item.Id == action.Target)
                    ?? throw new BusinessException("Dieses Plugin gibt es im Store nicht (mehr).");
                await StoreService.InstallAsync(listing.Item);
                return new LaunchOutcome(LaunchOutcomeKind.SetQuery, Query: action.Argument ?? listing.Item.Keyword);

            case LaunchActionKind.CloseProcess or LaunchActionKind.KillProcess:
                ProcessService.End(int.Parse(action.Target), force: action.Kind == LaunchActionKind.KillProcess);
                return LaunchOutcome.Done;

            case LaunchActionKind.System:
                Shell.Run(Enum.Parse<SystemCommand>(action.Target));
                return LaunchOutcome.Done;

            case LaunchActionKind.Script:
                return await PluginService.RunAsync(action.Target, action.Argument ?? "", isAction: true);

            case LaunchActionKind.SetQuery:
                return new LaunchOutcome(LaunchOutcomeKind.SetQuery, Query: action.Target);

            case LaunchActionKind.App:
                var command = Enum.Parse<AppCommand>(action.Target);
                if (command == AppCommand.Reindex)
                {
                    _ = AppIndex.Rescan();
                    return LaunchOutcome.Show("Die Programme werden neu eingelesen …");
                }

                return new LaunchOutcome(LaunchOutcomeKind.AppCommand, Command: command);

            default:
                return LaunchOutcome.Done;
        }
    }
}
