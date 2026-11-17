# Der Store von ThixFred

Was hier liegt, baut die CI (`.github/workflows/store.yml`, `node tools/store-build.js artifacts/store`) zum Store und
veröffentlicht ihn im Release `store-channel`; ThixFred liest dort `store.json`. Jedes Paket steht im Katalog mit seiner
SHA-256 – ThixFred prüft sie, bevor es etwas auspackt, und lässt keine Datei außerhalb des Plugin-Ordners zu.

## Ein Plugin

Ein Ordner `plugins/<id>/` mit `plugin.json` und den Skripten:

```json
{
  "id": "uuid",                       // = Ordnername: kleine Buchstaben, Ziffern, "-"
  "name": "UUID",
  "version": "1.0.0",                 // höher = Update für alle, die es installiert haben
  "author": "ThixFred",
  "description": "Erzeugt neue UUIDs – Enter kopiert.",
  "keyword": "uuid",                  // was im Launcher zuerst getippt wird
  "icon": "key",                      // ein Symbol aus MatDesigns Sprite
  "mode": "Filter",                   // Filter: läuft beim Tippen · Run: läuft mit Enter
  "output": "Launcher",               // nur Run: Launcher | Clipboard | Terminal | None
  "script": "uuid.py",                // überall – oder je System:
  "scriptWindows": "uuid.ps1",
  "scriptMacOS": "uuid.sh",
  "timeoutSeconds": 5
}
```

Das Skript bekommt den Text nach dem Schlüsselwort als erstes Argument und in `THIXFRED_QUERY`, seinen Ordner in
`THIXFRED_PLUGIN_DIR`; beim zweiten Schritt eines Filter-Ergebnisses mit `"action": "run"` steht `THIXFRED_ACTION=run`.
Ausgeführt wird nach Endung: `.ps1` PowerShell (pwsh, sonst Windows PowerShell), `.cmd`/`.bat` cmd, `.sh` bash, `.zsh` zsh,
`.py` Python 3, `.js` Node, `.exe` direkt.

**Filter** geben JSON aus – oder einfach eine Zeile je Ergebnis (Enter kopiert sie):

```json
{ "items": [ { "title": "…", "subtitle": "…", "arg": "…", "action": "copy | open | terminal | run | query", "icon": "…", "autocomplete": "…" } ] }
```

`copy` kopiert `arg`, `open` öffnet `arg` (Adresse, Datei, Programm), `terminal` führt `arg` im Terminal aus, `run` startet das
Skript erneut mit `arg`, `query` ersetzt die Suche durch `arg`.

## Ein Theme

Eine Datei `themes/<id>.json` mit den Feldern eines Themes (`name`, `isDark`, `backdrop` Solid | Acrylic | Mica,
`background`, `border`, `text`, `mutedText`, `selection`, `accent` als `#RRGGBB` / `#AARRGGBB`, `cornerRadius`, `fontSize`,
`width`, `padding`) und für den Store `version`, `author`, `description`.

## Ein eigener Store

Jede Adresse eines `store.json` in diesem Format geht (Einstellungen › Erweitert › Adresse des Stores) – z. B. der Store einer
Firma, gebaut mit demselben Skript aus einem eigenen `store/`-Ordner.
