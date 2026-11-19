using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using ThixFred.Core.Models;

namespace ThixFred.Core.Platform.Windows;

/// <summary>
/// File types of an app for the current user, without administrator rights (SETUP-08): under HKCU\Software\Classes a ProgID
/// "&lt;Product&gt;.&lt;ext&gt;" (name, icon, "open" = the program with the file), the app in "Öffnen mit" of the extension
/// (OpenWithProgids, Applications\&lt;exe&gt;\SupportedTypes) and the ProgID as the extension's handler. Windows protects the
/// choice a user made himself in "Öffnen mit" (UserChoice): it stays, the app is then offered there.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class FileAssociations
{
    private const string DefaultClassesRoot = @"Software\Classes";
    private static string _classesRoot = DefaultClassesRoot;

    /// <summary>Another key instead of HKCU\Software\Classes (tests); null = the real one.</summary>
    public static void UseClassesRootForTests(string? root) => _classesRoot = root ?? DefaultClassesRoot;

    public static string ProgId(string product, string extension) => $"{product}.{extension.TrimStart('.').ToLowerInvariant()}";

    /// <summary>Associates the file types with <paramref name="executable"/>.</summary>
    public static void Register(string product, string executable, IEnumerable<InstallerFileType> types)
    {
        var changed = false;
        var exeName = Path.GetFileName(executable);
        foreach (var type in types)
        {
            var progId = ProgId(product, type.Extension);
            using (var key = Registry.CurrentUser.CreateSubKey($@"{_classesRoot}\{progId}"))
            {
                key.SetValue("", type.Name);
                key.SetValue("FriendlyTypeName", type.Name);
            }

            using (var icon = Registry.CurrentUser.CreateSubKey($@"{_classesRoot}\{progId}\DefaultIcon"))
            {
                icon.SetValue("", $"\"{executable}\",0");
            }

            using (var command = Registry.CurrentUser.CreateSubKey($@"{_classesRoot}\{progId}\shell\open\command"))
            {
                command.SetValue("", $"\"{executable}\" \"%1\"");
            }

            using (var openWith = Registry.CurrentUser.CreateSubKey($@"{_classesRoot}\{type.Extension}\OpenWithProgids"))
            {
                openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
            }

            using (var extension = Registry.CurrentUser.CreateSubKey($@"{_classesRoot}\{type.Extension}"))
            {
                extension.SetValue("", progId);
            }

            using (var supported = Registry.CurrentUser.CreateSubKey($@"{_classesRoot}\Applications\{exeName}\SupportedTypes"))
            {
                supported.SetValue(type.Extension, "");
            }

            changed = true;
        }

        if (changed)
        {
            NotifyShell();
        }
    }

    /// <summary>Removes the app from these file types (what other apps registered stays).</summary>
    public static void Unregister(string product, IEnumerable<string> extensions)
    {
        var changed = false;
        foreach (var extension in extensions)
        {
            var progId = ProgId(product, extension);
            using (var progKey = Registry.CurrentUser.OpenSubKey($@"{_classesRoot}\{progId}"))
            {
                changed |= progKey is not null;
            }

            Registry.CurrentUser.DeleteSubKeyTree($@"{_classesRoot}\{progId}", throwOnMissingSubKey: false);
            using (var openWith = Registry.CurrentUser.OpenSubKey($@"{_classesRoot}\{extension}\OpenWithProgids", writable: true))
            {
                openWith?.DeleteValue(progId, throwOnMissingValue: false);
            }

            using (var key = Registry.CurrentUser.OpenSubKey($@"{_classesRoot}\{extension}", writable: true))
            {
                if (key?.GetValue("") is string handler && handler.Equals(progId, StringComparison.OrdinalIgnoreCase))
                {
                    key.DeleteValue("", throwOnMissingValue: false);
                }
            }

            using (var applications = Registry.CurrentUser.OpenSubKey($@"{_classesRoot}\Applications", writable: true))
            {
                foreach (var app in applications?.GetSubKeyNames().Where(name => name.StartsWith(product, StringComparison.OrdinalIgnoreCase)) ?? [])
                {
                    using var supported = applications!.OpenSubKey($@"{app}\SupportedTypes", writable: true);
                    supported?.DeleteValue(extension, throwOnMissingValue: false);
                }
            }
        }

        if (changed)
        {
            NotifyShell();
        }
    }

    /// <summary>The extensions (of <paramref name="candidates"/>) the app is associated with now.</summary>
    public static IReadOnlyList<string> Registered(string product, IEnumerable<string> candidates) =>
        candidates.Where(extension =>
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{_classesRoot}\{ProgId(product, extension)}\shell\open\command");
            return key is not null;
        }).ToList();

    /// <summary>Explorer reads the associations again (SHCNE_ASSOCCHANGED); not for the test key.</summary>
    private static void NotifyShell()
    {
        if (_classesRoot == DefaultClassesRoot)
        {
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
