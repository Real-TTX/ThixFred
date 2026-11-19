# CLAUDE.md

Guidance for working in this repository. Keep it in sync with the code: update it whenever architecture, interfaces or conventions change.

## What ThixFred is

A **launcher for Windows and macOS** (an alternative to Alfred, focused on launching): a global shortcut (default `Alt+Space`,
on macOS ⌥Space) opens one search field above everything; it starts and closes programs, calculates, runs `>commands` in a
terminal, opens addresses and web searches, runs system commands – and whatever plugins and the user's scripts do. Themes for
the launcher, a store of plugins and themes, and the **ThixCloud** (= **MatCloud**, the cloud of all Thix apps, sibling
repository `../MatCloud`, port 4950; ThixFred uses its *general sync*, app id `fred`) for the configuration. Tray / menu bar,
autostart, Windows setup, macOS app bundle, OTA updates with optional full auto update.
**It must be efficient (owner's requirement): the launcher is built once and only shown/hidden, the search runs in memory on
every key, nothing on that path touches disk or network.** UI language German; code, comments and identifiers English.

Stack: **.NET 10**, C#, **Avalonia 11.3** with **MatDesktop** (the desktop toolkit of MatDesign, copied), no database (JSON files),
no image library (icons: the shell / sips + an own PNG writer). The desktop app follows MatDesign's rules DESK, TRAY and SETUP.

## Build / run / test

- `dotnet build ThixFred.slnx` – expected: 0 warnings, 0 errors. On Windows the projects target `net10.0-windows10.0.19041.0`,
  elsewhere `net10.0` (`DesktopTargetFramework` in `Directory.Build.props`).
- `dotnet test tests/ThixFred.Tests` – search, calculator, keywords, usage, plugins, scripts, the store (fake HTTP: hashes, zip
  paths, updates), themes, web searches, sync against `FakeCloud` (MatCloud's general sync in memory), the store folder (on
  Windows the store plugins really run), and the **UI rendered headless** (`UiTests`: launcher keyboard flows, list and edit
  pages; `THIXFRED_SCREENSHOTS=<folder>` keeps screenshots – look at them). `TestData` isolates every test: own data folder,
  token store in memory, no policies, no icons, the sample program index, no process ends, nothing starts (`Shell.StartForTests`).
- **Snapshots:** `dotnet run --project src/ThixFred -- --snapshot docs/images` draws every window headless (light and dark, 1x):
  `launcher-{search,calc,terminal,console,kill,recent}`, `launcher-theme-<id>`, `main-{plugins,script,store,themes,theme-edit,websearches,
  settings,search,cloud,cloud-code,about}`, `setup-*` (Windows), `toast`, `tray-icon-{windows,macos}`. Temporary data folder with
  `SampleData` (programs without files, two scripts, a store plugin, a theme), pinned clock/version/computer, no icons, no
  network (`StoreService.ListingsForTests`), no real processes. A new window or page: add it to `Snapshot.cs` and look at the pictures.
- `ThixFred.exe --self-test <report>` checks the published (trimmed) program; CI runs it after every Windows publish.
- `THIXFRED_DATA_DIR=<folder>` runs an own instance with its own data (never meets the installed one).
- Icons: `cd tools && npm install && node render-icons.js` renders `branding/*.svg` (installed Edge) into `branding/png` and
  `src/ThixFred/Assets`. Line icons of the UI are MatDesktop's sprite (`<mat:MatIcon Icon="plug"/>`).
- Store: `node tools/store-build.js <folder>` builds `store/` into `store.json` + packages (deterministic zips, SHA-256).
- **MatDesktop** (`src/ThixFred/MatDesktop/`) and **the MatCloud contract** (`src/ThixFred.Core/MatCloud/`) are COPIES: never edit
  them here (tests hold them to their `matdesktop.version` / `matcloud.version`). MatDesktop: copy again from MatDesign
  (`node tools/matdesktop-copy.js <path>/ThixFred/src/ThixFred` there); contract: `node ../MatCloud/tools/contract-copy.js src/ThixFred.Core`.
- ThixFred is `CloudApps.Fred` ("fred") in MatCloud since MatCloud.Contract 1.1.0; an older MatCloud refuses its devices ("unknown app").
- `THIXFRED_CLOUD_URL` / `_USER` / `_PASSWORD` enable `CloudEndToEndTests` (two computers against a real MatCloud, e.g. the local
  stack, an account with a password).

## Map

```
src/ThixFred.Core       (no UI) models, business layer (static classes), infrastructure, native code
  MatCloud/             COPY of MatCloud's contract (sign-in, general sync SyncContract, MatCloudClient) – never edited here
  Models/               AppSettings (+ PluginPreference, CloudSettings), SearchResult (ResultKind, LaunchAction(Kind), ResultIcon,
                        LaunchOutcome, SystemCommand, AppCommand), LauncherTheme (+ ThemeBackdrop), WebSearch, Plugin.cs (PluginManifest,
                        PluginInfo, UserScript, StoreItem/Catalog/Listing, enums), Hotkey, BusinessException
  Business/             SearchService (THE search: keyword first – terminal ">", "kill", plugins, web search keywords –, then calculator,
                        AppIndex, system and ThixFred commands, addresses, web searches by the start of their keyword ("w x"),
                        the keyword of a store plugin not installed (from StoreService.KnownPlugins, memory only), plugins by
                        name, the web fallback; Rank adds usage; a filter plugin shows "Wird abgefragt …" until it answers),
                        IntegratedTerminal (the launcher's own terminal: one session, folder + output + history, cd/cls itself,
                        input, Strg+C kills the tree; the shell is Platform/CommandShell: cmd with UTF-8 / zsh -l, no PTY),
                        LaunchService (runs a LaunchAction, records usage, the clipboard as a delegate), FuzzyMatcher, Calculator,
                        AppIndex (+ AppEntry: in memory, cache apps.json, background scan, watchers), IconService (icon PNG cache,
                        one STA worker), ProcessService (running apps, close / kill), TerminalService, UsageService (usage.json:
                        counts, query → choice, last commands), WebSearchService, ThemeService (built-ins + own themes/*.json),
                        ScriptService (scripts/*.<ext> with @header), PluginService (built-ins + plugin folders + scripts,
                        prefs, keyword match, run / filter, JSON or line results), StoreService (catalog, install with SHA-256
                        and zip-path check, update, uninstall), ConfigSync (what goes to the cloud: collections settings, themes,
                        websearches, scripts, store; index sync.json) + SyncService (sign-in browser/password, the loop),
                        InstallerOptions (SETUP-08 of MatDesign: installer.json – embedded, and as "installer" in the update
                        manifest – says what the setup offers: desktop shortcut, autostart, automatic updates, file types; the
                        command line --quiet / --update / --configure [--desktop-shortcut=yes|no --autostart=yes|no
                        --automatic-updates=yes|no --file-types=.a,.b|none|default] [--start], a wrong value = exit code 2;
                        Current/Apply; file types: Platform/Windows/FileAssociations in HKCU\Software\Classes, Platform/Mac/
                        MacFileAssociations with Launch Services),
                        SettingsService, HotkeyService, AccessPolicy (LockConfiguration, DisableCloud, DisableUpdates,
                        DisableScripts, DisableStore), AutostartService, InstallService, UpdateService, MacAppBundle, SampleData,
                        Slug
  Infrastructure/       AppPaths, AppInfo, AppClock, JsonFile (atomic JSON), SettingsStore, TokenStore (DPAPI/Keychain), PngWriter,
                        Log, ProblemReports
  Platform/             Shell (open, reveal, runas, start, system commands), ScriptRunner (interpreter by extension, env
                        THIXFRED_QUERY/_PLUGIN_DIR/_ACTION, timeout), WindowsApps (Start menu .lnk via ShellLink, Get-StartApps, settings
                        pages), MacApps (.app bundles, German names), WindowActivation, Windows/ (WindowsKeyboardHook: the global
                        shortcut, swallowed, mask key for Win and Alt; ShellLink read/write; ShellIcons: IShellItemImageFactory),
                        Mac/ (MacHotkey Carbon, MacRunningApps NSWorkspace, ObjC)
src/ThixFred            (Avalonia app)
  Program / App         start-up: ProblemReports, self test, snapshot, Windows setup mode, single instance, theme (accent #5B33F0),
                        AppIndex, launcher + settings window + tray, hotkey, sync, updates; RunCommand(AppCommand), ShowLauncher
  Launcher/             LauncherWindow (frameless, topmost, show/hide, ALL launcher keys, confirmation and message states, filter
                        plugins debounced 120 ms, placement on the screen of the mouse, the terminal mode; its KIND is fixed when
                        built: Acrylic/Mica = the system's glass, Shadowed = own rounded border + shadow on a transparent window,
                        Flat = a plain window – a theme of another kind raises RebuildRequested and App builds a new launcher,
                        because Windows does not turn a glass window into a plain one cleanly), LauncherView (the themed content,
                        pooled rows, the terminal output – also the preview of the theme editor), IconImages
  MainWindow            the settings window: MatTitleBar ("Launcher öffnen", hotkey), MatNav (Plugins, Store, Themes, Websuchen;
                        Einstellungen; cloud state at the foot; version = About), page host with a back stack (OpenPage/GoBack, Esc)
  Pages/                PluginsPage + PluginEditPage + ScriptEditPage (tabs Allgemein | Skript | Testen), StorePage + StoreItemPage,
                        ThemesPage + ThemeEditPage (live preview), WebSearchesPage + WebSearchEditPage, SettingsPage (Allgemein |
                        Suche | Cloud | Updates | Erweitert), AboutPage
  Controls/             ONE code base per control: ListToolbar (search, filter, sort, the switch tiles / list), EntityList (+ EntityRow:
                        tiles in a TileGrid – icon or Banner on a band, a click opens, "add" tiles at the end – or card rows),
                        ListLayout (with a look key: tiles or rows, remembered per page in AppSettings.ListLooks, local only) and
                        EditLayout (the list page and the create/update page), ActionBar, FormErrors, IconLabel, InfoBar, KeyCaps,
                        HotkeyRecorder
  Tray/ Setup/ Dialogs/ TrayIcon (MatTray), ThixFredSetup (MatSetupWindow), ConfirmDialog, ProblemReportDialog
  MatDesktop/           COPY of MatDesign's desktop toolkit (never edited here)
store/                  the store's sources: plugins/<id>/plugin.json + scripts, themes/<id>.json (README there: the format)
tests/ThixFred.Tests    the tests; FakeCloud = MatCloud's general sync in memory
```

## Conventions

- **Microsoft C# coding conventions**, file-scoped namespaces, `_camelCase` fields, human-readable names, early returns, no deep nesting.
- **Business layer = static classes.** UI never touches files, registry, processes or HTTP; page models never query. Validation
  **and permission checks** (AccessPolicy) live in the business layer. Errors are `BusinessException(message, field)`; edit pages
  show them at the `MatFormField` whose `FormErrors.Field` is that field (`EditLayout.ShowError`, also on another tab).
  What the business layer must not do itself (the clipboard, opening windows) it gets as a delegate or returns as `LaunchOutcome`.
- **The search path is hot:** `SearchService.Search` is synchronous and in memory; prepare strings once (`AppEntry.Prepared`);
  nothing on it reads a file or starts a process – except filter plugins, which the launcher asks afterwards, cancellable.
- **UI rules (owner):** entities have a list page (toolbar with search, filter, sort ABOVE the list; the entries as tiles – the
  default, like a store, the last tile adds – or rows; actions BELOW, left-aligned, positive to negative) and a create/update page with tabs for its areas and one row of buttons (Speichern, Zurück … gap … Löschen);
  hide what does not apply (Ausgabe only for "Ausführen"); icons where they help; cards keep their distance; About opens from the
  version bottom left. **Colours only from MatDesktop's theme** (DESK-02) – except `Launcher/`, whose colours are the user's theme
  (data); `UiTests.No_window_writes_a_colour_of_its_own` holds that. Several instances of a control on one page must work.
- **Sync rules:** the general sync of MatCloud, last writer wins by `UpdatedAt`; the hotkey, window sizes, usage and the program
  index never leave the computer; never log scripts, passwords or tokens.
- **Scripts** are files with a comment header (`@name`, `@keyword`, `@mode`, `@output`, `@description`); `.ps1` is written with a
  BOM (Windows PowerShell 5.1), `.cmd` in CRLF, the rest in LF and executable on macOS.
- **Platforms:** one code base; differences behind `OperatingSystem.IsWindows()/IsMacOS()` or Windows/macOS-only types. macOS
  cannot be checked from the Windows machine: CI builds and tests it.
- Edit text files with the editor tools or Node (`fs`), UTF-8: Windows PowerShell 5.1 `Get-Content`/`Set-Content` double-encode
  umlauts; bash heredocs break on apostrophes in this environment.

## Versioning, branches, CI

- `Directory.Build.props` holds major/minor. CI sets `Channel`, `BuildNumber` (GitHub run number), `BuildDate`: release
  `0.1.42-20261117` (main), nightly `nightly-43-20261117` (dev), local `local-20261117` (everything else).
- Branches: `main` = release, `dev` = nightly. **Commits are authored by Matthias Schmoldt only – no co-author trailers, no
  other names, no "generated with" lines.**
- `.github/workflows/desktop.yml`: Windows (test, publish win-x64 trimmed single file = `ThixFredSetup.exe`, self test), macOS
  (test, osx-arm64 + osx-x64, `scripts/package-macos.sh` → dmg/zip), publish (manifest `thixfred-update.json`, rolling releases
  `release-channel` / `nightly-channel`, versioned release `v<version>` on main).
- `.github/workflows/store.yml`: builds `store/` (every branch, artifact) and publishes it on main in the release `store-channel`.
