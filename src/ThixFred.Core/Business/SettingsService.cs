using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred.Core.Business;

/// <summary>
/// Reading and changing the settings of this computer. Every change is checked here (values, policy) before it is
/// saved; the UI only offers. <see cref="Changed"/> tells the app (hotkey, theme, launcher) what is new.
/// </summary>
public static class SettingsService
{
    /// <summary>Raised after every saved change (any thread).</summary>
    public static event EventHandler<AppSettings>? Changed;

    public static AppSettings Get() => SettingsStore.Read();

    /// <summary>Changes general settings (not the cloud connection: <see cref="SyncService"/>).</summary>
    public static AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        AccessPolicy.EnsureConfigurationEditable();
        var current = SettingsStore.Read();
        var next = change(current) with { Cloud = current.Cloud };
        Validate(next);
        return Save(_ => next);
    }

    public static AppSettings SetHotkey(string text)
    {
        var hotkey = HotkeyService.Parse(text) ?? throw new BusinessException("Dieses Tastenkürzel wird nicht unterstützt.", nameof(AppSettings.Hotkey));
        HotkeyService.Validate(hotkey);
        return Update(settings => settings with { Hotkey = HotkeyService.Format(hotkey) });
    }

    /// <summary>A folder searched for programs in addition to the Start menu / Applications.</summary>
    public static AppSettings AddAppFolder(string folder)
    {
        var path = folder.Trim();
        if (!Directory.Exists(path))
        {
            throw new BusinessException("Diesen Ordner gibt es nicht.", nameof(AppSettings.ExtraAppFolders));
        }

        var settings = Update(current => current with
        {
            ExtraAppFolders = current.ExtraAppFolders.Contains(path, StringComparer.OrdinalIgnoreCase) ? current.ExtraAppFolders : [.. current.ExtraAppFolders, path],
        });
        _ = AppIndex.Rescan();
        return settings;
    }

    public static AppSettings RemoveAppFolder(string folder)
    {
        var settings = Update(current => current with { ExtraAppFolders = current.ExtraAppFolders.Where(existing => !string.Equals(existing, folder, StringComparison.OrdinalIgnoreCase)).ToList() });
        _ = AppIndex.Rescan();
        return settings;
    }

    /// <summary>Things the app remembers by itself (window size, folded menu, welcome shown, the last search): no configuration lock.</summary>
    public static AppSettings Remember(Func<AppSettings, AppSettings> change)
    {
        var current = SettingsStore.Read();
        var next = change(current);
        return Save(_ => current with
        {
            WindowWidth = Math.Clamp(next.WindowWidth, 640, 4000),
            WindowHeight = Math.Clamp(next.WindowHeight, 420, 3000),
            IsSidebarCollapsed = next.IsSidebarCollapsed,
            WelcomeShown = next.WelcomeShown,
            LastQuery = current.KeepLastQuery ? next.LastQuery : "",
        });
    }

    /// <summary>The cloud part (only <see cref="SyncService"/>).</summary>
    internal static AppSettings UpdateCloud(Func<CloudSettings, CloudSettings> change) =>
        Save(settings => settings with { Cloud = change(settings.Cloud) });

    /// <summary>The settings of the account as the cloud has them (no lock: they come from the account; checked all the same).</summary>
    internal static void ApplyFromCloud(Func<AppSettings, AppSettings> change)
    {
        var current = SettingsStore.Read();
        var next = change(current) with { Cloud = current.Cloud };
        try
        {
            Validate(next);
        }
        catch (BusinessException exception)
        {
            Log.Warning($"Settings from the cloud refused: {exception.Message}");
            return;
        }

        if (next != current)
        {
            Save(_ => next);
            PluginService.Invalidate();
        }
    }

    private static AppSettings Save(Func<AppSettings, AppSettings> change)
    {
        var saved = SettingsStore.Update(change);
        Changed?.Invoke(null, saved);
        return saved;
    }

    private static void Validate(AppSettings settings)
    {
        var hotkey = HotkeyService.Parse(settings.Hotkey) ?? throw new BusinessException("Dieses Tastenkürzel wird nicht unterstützt.", nameof(AppSettings.Hotkey));
        HotkeyService.Validate(hotkey);
        if (settings.MaxResults is < 3 or > 20)
        {
            throw new BusinessException("Es werden 3 bis 20 Ergebnisse gezeigt.", nameof(AppSettings.MaxResults));
        }

        if (settings.StoreUrl.Length > 0 && (!Uri.TryCreate(settings.StoreUrl, UriKind.Absolute, out var store) || store.Scheme != "https"))
        {
            throw new BusinessException("Die Adresse des Stores beginnt mit https://.", nameof(AppSettings.StoreUrl));
        }

        if (!Enum.IsDefined(settings.Terminal) || !Enum.IsDefined(settings.Theme))
        {
            throw new BusinessException("Diese Auswahl gibt es nicht.");
        }
    }
}
