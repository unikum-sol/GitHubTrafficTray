using System.Net;

namespace GitHubTrafficTray;

public sealed class RepoTraffic
{
    public string RepoName { get; set; } = "";
    public GhClones? Clones { get; set; }
    public GhViews? Views { get; set; }
    public List<GhReferrer>? Referrers { get; set; }
    public List<GhPath>? Paths { get; set; }
}

public sealed class FetchResult
{
    public List<GhRepo> Repos { get; } = new();
    public bool RepoListComplete { get; set; }
    public List<RepoTraffic> Traffic { get; } = new();
    public List<string> Errors { get; } = new();
}

internal static class TrafficService
{
    public static async Task<FetchResult> FetchAsync(GitHubClient gh, IProgress<string>? progress, CancellationToken ct)
    {
        var result = new FetchResult();
        string owner = AppConfig.Owner;
        string ownerEsc = Uri.EscapeDataString(owner);

        // 1) Repo-Liste: Organisations-Endpunkt + Benutzer-Endpunkt (gefiltert), damit es
        //    sowohl für eine Organisation als auch für ein Benutzerkonto "unikum-sol" funktioniert.
        progress?.Report("Lade Repo-Liste …");
        var repos = new Dictionary<string, GhRepo>(StringComparer.OrdinalIgnoreCase);
        var listErrors = new List<string>();

        try
        {
            foreach (var r in await gh.GetAllPagesAsync<GhRepo>($"orgs/{ownerEsc}/repos?type=all&per_page=100", ct))
                repos[r.Name] = r;
        }
        catch (GitHubApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Kein Organisationskonto -> kein Fehler
        }
        catch (Exception ex) when (IsRecoverable(ex, ct))
        {
            listErrors.Add("Repo-Liste (Organisation): " + Describe(ex));
        }

        try
        {
            foreach (var r in await gh.GetAllPagesAsync<GhRepo>(
                         "user/repos?per_page=100&affiliation=owner,collaborator,organization_member", ct))
            {
                if (string.Equals(r.Owner?.Login, owner, StringComparison.OrdinalIgnoreCase))
                    repos[r.Name] = r;
            }
        }
        catch (Exception ex) when (IsRecoverable(ex, ct))
        {
            listErrors.Add("Repo-Liste (Benutzer): " + Describe(ex));
        }

        if (repos.Count == 0 && listErrors.Count > 0)
            throw new InvalidOperationException(string.Join(" | ", listErrors));

        result.Errors.AddRange(listErrors);
        result.RepoListComplete = listErrors.Count == 0;
        result.Repos.AddRange(repos.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase));

        // 2) Traffic je Repo
        int i = 0;
        foreach (var repo in result.Repos)
        {
            ct.ThrowIfCancellationRequested();
            i++;
            progress?.Report($"Repo {i}/{result.Repos.Count}: {repo.Name}");

            string basePath = $"repos/{ownerEsc}/{Uri.EscapeDataString(repo.Name)}/traffic";
            var t = new RepoTraffic { RepoName = repo.Name };

            try
            {
                t.Clones = await gh.GetAsync<GhClones>($"{basePath}/clones?per=day", ct);
                t.Views = await gh.GetAsync<GhViews>($"{basePath}/views?per=day", ct);
                t.Referrers = await gh.GetAsync<List<GhReferrer>>($"{basePath}/popular/referrers", ct);
                t.Paths = await gh.GetAsync<List<GhPath>>($"{basePath}/popular/paths", ct);
            }
            catch (Exception ex) when (IsRecoverable(ex, ct))
            {
                result.Errors.Add($"{repo.Name}: {Describe(ex)}");
            }

            // Teilergebnisse (z. B. Clones ok, Views fehlgeschlagen) werden trotzdem übernommen
            result.Traffic.Add(t);
        }

        return result;
    }

    /// <summary>401 und Abbruch werden nicht pro Repo abgefangen, sondern beenden den gesamten Abruf.</summary>
    private static bool IsRecoverable(Exception ex, CancellationToken ct)
    {
        if (ex is GitHubApiException { StatusCode: HttpStatusCode.Unauthorized }) return false;
        if (ex is OperationCanceledException && ct.IsCancellationRequested) return false;
        return true;
    }

    public static string Describe(Exception ex) => ex switch
    {
        GitHubApiException { StatusCode: HttpStatusCode.Unauthorized } =>
            "401 – Token ungültig oder abgelaufen",
        GitHubApiException { StatusCode: HttpStatusCode.Forbidden } g =>
            $"403 – kein Zugriff ({g.Message}). Token braucht „Administration: Read-only“ für dieses Repo, oder Rate-Limit erreicht.",
        GitHubApiException { StatusCode: HttpStatusCode.NotFound } g =>
            $"404 – nicht gefunden oder kein Zugriff ({g.Message})",
        GitHubApiException g =>
            $"{(int)g.StatusCode} – {g.Message}",
        TaskCanceledException =>
            "Zeitüberschreitung der Anfrage",
        HttpRequestException h =>
            $"Netzwerkfehler: {h.Message}",
        _ => ex.Message
    };
}
