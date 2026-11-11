# GitHub Traffic Tray (unikum-sol)

Windows tray tool. Once a day it collects the traffic data of all repositories under `unikum-sol`
(clones, unique cloners, views, unique visitors, top referrers, popular paths) and stores it permanently on the local machine.

## Building
1. Requirement: .NET 10 **SDK** (a runtime alone is not sufficient). Check with: `dotnet --list-sdks`
2. Double-click `build.bat`
3. Output: `publish\GitHubTrafficTray.exe` (single file; the .NET 10 Desktop Runtime is required to run it)

## Creating the token (fine-grained)
GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens → Generate new token
- **Resource owner:** `unikum-sol`
- **Repository access:** *All repositories*, so that new repositories are picked up automatically
  (with *Only select repositories* the tool only sees the selected repositories)
- **Repository permissions → Administration:** *Read-only* (Metadata: Read-only is set automatically)
- If `unikum-sol` is an organization, it must allow fine-grained tokens or approve the token.

The token is requested on every start and is only kept in memory.

## Usage
- **Left-click** the tray icon: open the overview
- **Right-click**: detailed status, error list, "Jetzt abrufen" (fetch now), "Beenden" (exit)
- **X** in the window: the window is only hidden, the program keeps running in the tray
- **"Täglicher Abruf um"** (daily fetch at): selectable from 01:00 to 23:00 (default 08:00)
- **"Alles außer den letzten 30 Tagen löschen"** (delete everything except the last 30 days): removes older daily values and referrer/path snapshots (asks for confirmation first)

## Behavior
- Data is fetched once a day at the configured hour. If the PC is not running at that time, the fetch is performed after the program starts.
- If the scheduled fetch fails, it is retried after 30 minutes.
- GitHub always returns the last 14 days. Daily values are stored per date (UTC) and updated on every fetch.
  Older days are kept permanently. If fetching fails for a few days (fewer than 14), no data is lost.
- GitHub only provides top referrers and popular paths as a 14-day total. The tool stores one snapshot per fetch day and shows the most recent one.
- New repositories are added automatically on the next fetch. Deleted or renamed repositories keep their data and are marked as "nicht mehr gefunden" (no longer found).
- Unique values: for a single repository with "Letzte 14 Tage" (last 14 days), the heading shows the actual 14-day value from GitHub.
  In all other cases it is the sum of the daily unique values, i.e. not a count of distinct people.

## Data location
`%LOCALAPPDATA%\GitHubTrafficTray\`
- `traffic-data.json` – all collected data
- `settings.json` – fetch hour, last fetch
