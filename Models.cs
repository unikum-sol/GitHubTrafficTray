using System.Globalization;
using System.Text.Json.Serialization;

namespace GitHubTrafficTray;

internal static class AppConfig
{
    public const string Owner = "unikum-sol";
    public const string AppName = "GitHub Traffic Tray";
    public const int DefaultFetchHour = 8;
    public const int RetryMinutesAfterError = 30;
    public const int PurgeKeepDays = 30;

    public static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitHubTrafficTray");

    public static string DataFile => Path.Combine(DataDirectory, "traffic-data.json");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string DateKey(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateTime ParseDateKey(string key) =>
        DateTime.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None);
}

// ---------------------------------------------------------------------------
// GitHub-API-Antworten
// ---------------------------------------------------------------------------

public sealed class GhOwner
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
}

public sealed class GhUser
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
}

public sealed class GhRepo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
    [JsonPropertyName("owner")] public GhOwner? Owner { get; set; }
    [JsonPropertyName("private")] public bool Private { get; set; }
    [JsonPropertyName("fork")] public bool Fork { get; set; }
    [JsonPropertyName("archived")] public bool Archived { get; set; }
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
}

public sealed class GhTrafficPoint
{
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("uniques")] public int Uniques { get; set; }
}

public sealed class GhClones
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("uniques")] public int Uniques { get; set; }
    [JsonPropertyName("clones")] public List<GhTrafficPoint> Clones { get; set; } = new();
}

public sealed class GhViews
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("uniques")] public int Uniques { get; set; }
    [JsonPropertyName("views")] public List<GhTrafficPoint> Views { get; set; } = new();
}

public sealed class GhReferrer
{
    [JsonPropertyName("referrer")] public string Referrer { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("uniques")] public int Uniques { get; set; }
}

public sealed class GhPath
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("uniques")] public int Uniques { get; set; }
}

// ---------------------------------------------------------------------------
// Lokal gespeicherte Daten
// ---------------------------------------------------------------------------

public sealed class DayStat
{
    public int Count { get; set; }
    public int Uniques { get; set; }
}

public sealed class Totals14
{
    public int Count { get; set; }
    public int Uniques { get; set; }
    public DateTime FetchedUtc { get; set; }
}

public sealed class ReferrerEntry
{
    public string Referrer { get; set; } = "";
    public int Count { get; set; }
    public int Uniques { get; set; }
}

public sealed class PathEntry
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public int Count { get; set; }
    public int Uniques { get; set; }
}

public sealed class RepoRecord
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public bool IsPrivate { get; set; }
    public bool IsFork { get; set; }
    public bool IsArchived { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>Schlüssel: Datum (UTC) im Format yyyy-MM-dd</summary>
    public SortedDictionary<string, DayStat> Clones { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, DayStat> Views { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Schlüssel: Abrufdatum (UTC). Wert: von GitHub gelieferte 14-Tage-Liste.</summary>
    public SortedDictionary<string, List<ReferrerEntry>> ReferrerSnapshots { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, List<PathEntry>> PathSnapshots { get; set; } = new(StringComparer.Ordinal);

    public Totals14? LastClones14 { get; set; }
    public Totals14? LastViews14 { get; set; }
}

public sealed class TrafficStore
{
    public int Version { get; set; } = 1;
    public DateTime? LastCompleteRepoListUtc { get; set; }
    public Dictionary<string, RepoRecord> Repos { get; set; } = new();

    public void Normalize()
    {
        Repos ??= new();
        foreach (var key in Repos.Keys.ToList())
        {
            var r = Repos[key];
            if (r is null) { Repos.Remove(key); continue; }
            if (string.IsNullOrEmpty(r.Name)) r.Name = key;
            r.Clones ??= new(StringComparer.Ordinal);
            r.Views ??= new(StringComparer.Ordinal);
            r.ReferrerSnapshots ??= new(StringComparer.Ordinal);
            r.PathSnapshots ??= new(StringComparer.Ordinal);
        }
    }

    /// <summary>Übernimmt einen Abruf. Gibt die Namen neu entdeckter Repos zurück.</summary>
    public List<string> Merge(FetchResult result, DateTime nowUtc)
    {
        var newRepos = new List<string>();
        string snapshotKey = AppConfig.DateKey(nowUtc);

        foreach (var repo in result.Repos)
        {
            if (!Repos.TryGetValue(repo.Name, out var rec))
            {
                rec = new RepoRecord { Name = repo.Name, FirstSeenUtc = nowUtc };
                Repos[repo.Name] = rec;
                newRepos.Add(repo.Name);
            }
            rec.FullName = repo.FullName;
            rec.HtmlUrl = repo.HtmlUrl;
            rec.IsPrivate = repo.Private;
            rec.IsFork = repo.Fork;
            rec.IsArchived = repo.Archived;
            rec.LastSeenUtc = nowUtc;
        }

        if (result.RepoListComplete)
            LastCompleteRepoListUtc = nowUtc;

        foreach (var t in result.Traffic)
        {
            if (!Repos.TryGetValue(t.RepoName, out var rec)) continue;

            if (t.Clones is not null)
            {
                foreach (var p in t.Clones.Clones)
                    rec.Clones[AppConfig.DateKey(p.Timestamp.UtcDateTime)] = new DayStat { Count = p.Count, Uniques = p.Uniques };
                rec.LastClones14 = new Totals14 { Count = t.Clones.Count, Uniques = t.Clones.Uniques, FetchedUtc = nowUtc };
            }

            if (t.Views is not null)
            {
                foreach (var p in t.Views.Views)
                    rec.Views[AppConfig.DateKey(p.Timestamp.UtcDateTime)] = new DayStat { Count = p.Count, Uniques = p.Uniques };
                rec.LastViews14 = new Totals14 { Count = t.Views.Count, Uniques = t.Views.Uniques, FetchedUtc = nowUtc };
            }

            if (t.Referrers is not null)
                rec.ReferrerSnapshots[snapshotKey] = t.Referrers
                    .Select(r => new ReferrerEntry { Referrer = r.Referrer, Count = r.Count, Uniques = r.Uniques })
                    .ToList();

            if (t.Paths is not null)
                rec.PathSnapshots[snapshotKey] = t.Paths
                    .Select(p => new PathEntry { Path = p.Path, Title = p.Title, Count = p.Count, Uniques = p.Uniques })
                    .ToList();
        }

        return newRepos;
    }

    /// <summary>Löscht alle Einträge mit Datum vor cutoffKey (yyyy-MM-dd). Gibt die Anzahl gelöschter Einträge zurück.</summary>
    public int PurgeBefore(string cutoffKey)
    {
        int removed = 0;
        foreach (var rec in Repos.Values)
        {
            removed += RemoveOlder(rec.Clones, cutoffKey);
            removed += RemoveOlder(rec.Views, cutoffKey);
            removed += RemoveOlder(rec.ReferrerSnapshots, cutoffKey);
            removed += RemoveOlder(rec.PathSnapshots, cutoffKey);
        }
        return removed;
    }

    private static int RemoveOlder<T>(SortedDictionary<string, T> dict, string cutoffKey)
    {
        var keys = dict.Keys.Where(k => string.CompareOrdinal(k, cutoffKey) < 0).ToList();
        foreach (var k in keys) dict.Remove(k);
        return keys.Count;
    }
}

public sealed class AppSettings
{
    public int FetchHour { get; set; } = AppConfig.DefaultFetchHour;

    /// <summary>Lokales Datum (yyyy-MM-dd) des letzten geplanten Abrufs.</summary>
    public string? LastScheduledFetchDate { get; set; }

    public DateTime? LastSuccessfulFetchUtc { get; set; }
}

public sealed class RateLimitInfo
{
    public int Limit { get; set; }
    public int Remaining { get; set; }
    public DateTime ResetLocal { get; set; }
}

public sealed class AppStatus
{
    public bool IsFetching { get; set; }
    public string? Progress { get; set; }
    public DateTime? LastStartLocal { get; set; }
    public DateTime? LastEndLocal { get; set; }
    public bool? LastSuccess { get; set; }
    public string? LastKind { get; set; }
    public string? LastError { get; set; }
    public List<string> RepoErrors { get; set; } = new();
    public List<string> NewRepos { get; set; } = new();
    public string TokenState { get; set; } = "ungeprüft";
    public string? UserLogin { get; set; }
    public DateTime? NextRetryLocal { get; set; }
}
