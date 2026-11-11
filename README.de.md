# GitHub Traffic Tray (unikum-sol)

Tray-Tool für Windows. Es sammelt einmal täglich die Traffic-Daten aller Repos unter `unikum-sol`
(Clones, Unique Cloners, Views, Unique Visitors, Top-Referrer, Beliebte Pfade) und speichert sie dauerhaft lokal.

## Kompilieren
1. Voraussetzung: .NET 10 **SDK** (eine reine Runtime reicht nicht). Prüfen mit: `dotnet --list-sdks`
2. `build.bat` doppelklicken
3. Ergebnis: `publish\GitHubTrafficTray.exe` (eine Datei; zum Ausführen wird die .NET 10 Desktop Runtime benötigt)

## Token anlegen (Fine-grained)
GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens → Generate new token
- **Resource owner:** `unikum-sol`
- **Repository access:** *All repositories*, damit neue Repos automatisch erfasst werden
  (bei *Only select repositories* sieht das Tool nur die ausgewählten Repos)
- **Repository permissions → Administration:** *Read-only* (Metadata: Read-only wird automatisch gesetzt)
- Falls `unikum-sol` eine Organisation ist, muss sie Fine-grained Tokens zulassen bzw. den Token genehmigen.

Der Token wird bei jedem Start abgefragt und nur im Arbeitsspeicher gehalten.

## Bedienung
- **Linksklick** auf das Tray-Symbol: Übersicht öffnen
- **Rechtsklick**: detaillierter Status, Fehlerliste, „Jetzt abrufen“, „Beenden“
- **X** im Fenster: Fenster wird nur ausgeblendet, das Programm läuft im Tray weiter
- **Täglicher Abruf um:** 01:00 bis 23:00 Uhr wählbar (Standard 08:00 Uhr)
- **Alles außer den letzten 30 Tagen löschen:** entfernt ältere Tageswerte und Referrer-/Pfad-Stände (mit Rückfrage)

## Verhalten
- Abruf einmal täglich zur eingestellten Stunde. Läuft der PC zu dieser Zeit nicht, wird der Abruf nach dem Start nachgeholt.
- Schlägt der geplante Abruf fehl, folgt nach 30 Minuten ein neuer Versuch.
- GitHub liefert immer die letzten 14 Tage. Tageswerte werden pro Datum (UTC) gespeichert und bei jedem Abruf aktualisiert.
  Ältere Tage bleiben dauerhaft erhalten. Wenn der Abruf einige Tage ausfällt (weniger als 14), gehen keine Daten verloren.
- Top-Referrer und Beliebte Pfade liefert GitHub nur als 14-Tage-Summe. Das Tool speichert pro Abruftag einen Stand und zeigt den neuesten an.
- Neue Repos werden beim nächsten Abruf automatisch aufgenommen. Gelöschte oder umbenannte Repos behalten ihre Daten und werden als „nicht mehr gefunden“ markiert.
- Unique-Werte: Bei einem einzelnen Repo und „Letzte 14 Tage“ zeigt die Überschrift den echten 14-Tage-Wert von GitHub.
  In allen anderen Fällen ist es die Summe der täglichen Unique-Werte, also keine eindeutige Personenanzahl.

## Datenablage
`%LOCALAPPDATA%\GitHubTrafficTray\`
- `traffic-data.json` – alle gesammelten Daten
- `settings.json` – Abrufzeit, letzter Abruf
