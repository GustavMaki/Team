namespace Tharga.Team;

/// <summary>Reading every page of a team list, for a caller that wants them all but not their rosters.</summary>
public static class TeamPageExtensions
{
    /// <summary>Every team the caller's directory read returns, page by page, without rosters.</summary>
    public static async Task<TeamSummary[]> GetAllTeamSummariesAsync(this ITeamDirectoryService directory, CancellationToken cancellationToken = default)
    {
        var teams = new List<TeamSummary>();
        string cursor = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await directory.GetTeamPageAsync(new TeamPageRequest(cursor, TeamPageRequest.MaxPageSize));
            teams.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor != null);

        return [.. teams];
    }

    /// <summary>Every one of the caller's own teams, page by page, without rosters and unfiltered by <c>team:read</c>.</summary>
    public static async Task<TeamSummary[]> GetAllTeamSummariesAsync(this ITeamService service, CancellationToken cancellationToken = default)
    {
        var teams = new List<TeamSummary>();
        string cursor = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await service.GetTeamPageAsync(new TeamPageRequest(cursor, TeamPageRequest.MaxPageSize));
            teams.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor != null);

        return [.. teams];
    }
}
