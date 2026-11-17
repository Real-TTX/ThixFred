# ThixFred – Logo

Die Leiste des Launchers – Prompt `>`, getippter Text und Cursor – auf einer violetten Kachel, darunter die Ergebnisse.
Akzentfarbe **#5B33F0** (weißer Text 7:1), Verlauf der Kachel **#9A7DFF → #4B26E3**.

| Datei | Detailstufe | Einsatz |
|---|---|---|
| `logo-icon.svg` | voll (ab 48 px): Leiste mit Prompt, Text und Cursor, Ergebnisse darunter, Schatten | App-Icon (Windows .ico ab 48 px, macOS .icns), Fenster, Über-Seite |
| `logo-small.svg` | reduziert (16–32 px): nur `>_`, dick, auf der Kachel | Favicon, kleine .ico-Größen, Taskleiste, Tray (Windows) |
| `logo-tray.svg` | einfarbig, ohne Kachel: Leiste mit `>_` als Umriss | macOS-Menüleiste (Template-Bild, das System färbt es) |
| `logo-cloud.svg` | voll, randlose Kachel mit Wolke | Kachel für Startbildschirme (auch „maskable“): `png/thixfred-cloud-*.png` |
| `logo-full.svg` | Icon + Wortmarke „Thix**Fred**“ | README, Kopfzeilen, Über-Seiten |

`png/` enthält alle gerenderten Größen. Neu erzeugen (nach jeder Änderung an einer SVG):

```
cd tools && npm install && node render-icons.js
```

Das Skript rendert mit dem installierten Microsoft Edge (headless) und schreibt zusätzlich
`src/ThixFred/Assets/*` (thixfred.ico, Tray-Icons, Logos).
Die macOS-Varianten `png/thixfred-mac-*.png` halten Apples Rand um die Kachel ein; `scripts/package-macos.sh` baut daraus das `.icns`.
