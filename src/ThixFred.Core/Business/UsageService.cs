using ThixFred.Core.Infrastructure;

namespace ThixFred.Core.Business;

/// <summary>
/// What the launcher learns on this computer (usage.json, never sent anywhere): how often a result was chosen, which result was
/// chosen for which typed text ("ch" → Chrome), and the last terminal commands. The search raises those results; with
/// "Aus Nutzung lernen" off nothing is recorded or used.
/// </summary>
public static class UsageService
{
    private const int MaxCommands = 30;
    private const int MaxQueries = 2000;

    private static readonly object Gate = new();
    private static UsageData? _data;
    private static Timer? _saveTimer;

    /// <summary>The boost of a result for a typed text: up to 30 for "often", up to 45 more for "chosen for exactly this".</summary>
    public static double Boost(string resultId, string preparedQuery)
    {
        if (!SettingsService.Get().LearnFromUsage)
        {
            return 0;
        }

        lock (Gate)
        {
            var data = Data;
            double boost = 0;
            if (data.Items.TryGetValue(resultId, out var item))
            {
                boost += Math.Min(30, 8 * Math.Log2(1 + item.Count));
            }

            if (preparedQuery.Length > 0 && data.Queries.TryGetValue(preparedQuery, out var chosen) && chosen.TryGetValue(resultId, out var times))
            {
                boost += Math.Min(45, 25 + 10 * Math.Log2(times));
            }

            return boost;
        }
    }

    /// <summary>A result was chosen for a typed text.</summary>
    public static void Record(string resultId, string preparedQuery)
    {
        if (!SettingsService.Get().LearnFromUsage)
        {
            return;
        }

        lock (Gate)
        {
            var data = Data;
            data.Items.TryGetValue(resultId, out var item);
            data.Items[resultId] = new UsageItem((item?.Count ?? 0) + 1, AppClock.UtcNow);
            if (preparedQuery.Length > 0)
            {
                if (!data.Queries.TryGetValue(preparedQuery, out var chosen))
                {
                    if (data.Queries.Count >= MaxQueries)
                    {
                        data.Queries.Remove(data.Queries.Keys.First());
                    }

                    chosen = [];
                    data.Queries[preparedQuery] = chosen;
                }

                chosen[resultId] = chosen.GetValueOrDefault(resultId) + 1;
            }
        }

        ScheduleSave();
    }

    /// <summary>The results chosen last, newest first (shown while the search is empty).</summary>
    public static IReadOnlyList<string> Recent(int count)
    {
        if (!SettingsService.Get().LearnFromUsage)
        {
            return [];
        }

        lock (Gate)
        {
            return Data.Items.OrderByDescending(pair => pair.Value.LastUsed).Take(count).Select(pair => pair.Key).ToList();
        }
    }

    public static void RememberCommand(string command)
    {
        lock (Gate)
        {
            var commands = Data.Commands;
            commands.Remove(command);
            commands.Insert(0, command);
            if (commands.Count > MaxCommands)
            {
                commands.RemoveRange(MaxCommands, commands.Count - MaxCommands);
            }
        }

        ScheduleSave();
    }

    public static IReadOnlyList<string> RecentCommands()
    {
        lock (Gate)
        {
            return Data.Commands.ToList();
        }
    }

    /// <summary>"Gelerntes vergessen" in the settings.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            _data = new UsageData();
        }

        Save();
    }

    /// <summary>Writes what is pending now (at the end of the app).</summary>
    public static void Flush()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        Save();
    }

    /// <summary>Tests start with nothing learned.</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _data = null;
        }
    }

    private static UsageData Data => _data ??= JsonFile.Read<UsageData>(AppPaths.UsageFile) ?? new UsageData();

    private static void ScheduleSave()
    {
        _saveTimer ??= new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
        _saveTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    private static void Save()
    {
        try
        {
            lock (Gate)
            {
                if (_data is not null)
                {
                    JsonFile.Write(AppPaths.UsageFile, _data);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"usage.json could not be written ({exception.Message})");
        }
    }

    private sealed class UsageData
    {
        public Dictionary<string, UsageItem> Items { get; set; } = [];

        public Dictionary<string, Dictionary<string, int>> Queries { get; set; } = [];

        public List<string> Commands { get; set; } = [];
    }

    private sealed record UsageItem(int Count, DateTimeOffset LastUsed);
}
