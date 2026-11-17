namespace Tharga.Team;

/// <summary>
/// A team as a list shows it: who it is and what consent it holds, without its members.
/// </summary>
/// <remarks>
/// It is an <see cref="ITeam"/>, so anything that takes one needs no change. The roster is the expensive part of a team
/// to read, and a list that only names teams has no use for it; read one team's members when that team is opened,
/// through <c>GetTeamByKeyAsync</c>.
/// </remarks>
public sealed record TeamSummary : ITeam
{
    public string Key { get; init; }
    public string Name { get; init; }
    public string Icon { get; init; }

    /// <inheritdoc cref="ITeam.ConsentedRoles"/>
    public string[] ConsentedRoles { get; init; }

    /// <inheritdoc cref="ITeam.ConsentAccessLevel"/>
    public AccessLevel? ConsentAccessLevel { get; init; }

    /// <inheritdoc cref="ITeam.TemporaryConsent"/>
    public TemporaryConsent TemporaryConsent { get; init; }

    /// <summary>The summary of a team, leaving its members behind.</summary>
    public static TeamSummary Of(ITeam team) => new()
    {
        Key = team.Key,
        Name = team.Name,
        Icon = team.Icon,
        ConsentedRoles = team.ConsentedRoles,
        ConsentAccessLevel = team.ConsentAccessLevel,
        TemporaryConsent = team.TemporaryConsent
    };
}

/// <summary>Which teams to read: the page after <see cref="Cursor"/>, at most <see cref="PageSize"/> of them.</summary>
/// <param name="Cursor">The <see cref="TeamPage.NextCursor"/> of the previous page; null or empty for the first.</param>
/// <param name="PageSize">How many teams at most. Clamped to 1 to 500.</param>
public sealed record TeamPageRequest(string Cursor = null, int PageSize = TeamPageRequest.DefaultPageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;

    /// <summary><see cref="PageSize"/> held to what a page may be.</summary>
    public int Size => Math.Clamp(PageSize, 1, MaxPageSize);
}

/// <summary>A page of teams, by name and then key, and where the next one starts.</summary>
/// <param name="Items">The teams of this page.</param>
/// <param name="NextCursor">Passed back as <see cref="TeamPageRequest.Cursor"/> to read on; null when this was the last page.</param>
public sealed record TeamPage(IReadOnlyList<TeamSummary> Items, string NextCursor);

/// <summary>
/// What a store hands the service for a page: the teams with, for each, only the caller's own membership, which is all
/// the read filter needs. Never shown to a caller.
/// </summary>
/// <param name="Teams">The teams of the page. <c>Members</c> holds at most the caller's own entry.</param>
/// <param name="NextCursor">Where the next page starts; null when this was the last.</param>
public sealed record TeamPageOf<TMember>(IReadOnlyList<ITeam<TMember>> Teams, string NextCursor)
    where TMember : ITeamMember;

/// <summary>The order and cursor of a page of teams, in one place so every store and the fallback agree.</summary>
public static class TeamPaging
{
    private const char Separator = '\u001f';

    /// <summary>The cursor that names <paramref name="team"/> as the last one read.</summary>
    public static string CursorOf(ITeam team)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{team.Name}{Separator}{team.Key}"));

    /// <summary>Reads a cursor: the name and key of the last team read, or null for the first page.</summary>
    /// <exception cref="ArgumentException">The cursor was not issued by this read.</exception>
    public static (string Name, string Key)? Parse(string cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;

        try
        {
            var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var at = text.IndexOf(Separator);
            if (at >= 0) return (text[..at], text[(at + 1)..]);
        }
        catch (FormatException)
        {
        }

        throw new ArgumentException($"'{cursor}' is not a cursor a team read issued.", nameof(cursor));
    }

    /// <summary>Whether <paramref name="team"/> comes after the team the cursor names.</summary>
    public static bool IsAfter(ITeam team, (string Name, string Key)? after)
    {
        if (after == null) return true;

        var byName = string.CompareOrdinal(team.Name ?? string.Empty, after.Value.Name);
        return byName > 0 || (byName == 0 && string.CompareOrdinal(team.Key, after.Value.Key) > 0);
    }

    /// <summary>The teams in the order a page reads them.</summary>
    public static IEnumerable<T> InOrder<T>(IEnumerable<T> teams) where T : ITeam
        => teams.OrderBy(x => x.Name ?? string.Empty, StringComparer.Ordinal).ThenBy(x => x.Key, StringComparer.Ordinal);

    /// <summary>The page of <paramref name="request"/> out of <paramref name="teams"/>, however they were read.</summary>
    public static (IReadOnlyList<T> Items, string NextCursor) Slice<T>(IEnumerable<T> teams, TeamPageRequest request)
        where T : ITeam
    {
        request ??= new TeamPageRequest();
        var after = Parse(request.Cursor);
        var size = request.Size;

        var found = InOrder(teams).Where(x => IsAfter(x, after)).Take(size + 1).ToArray();
        var page = found.Take(size).ToArray();

        return (page, found.Length > size ? CursorOf(page[^1]) : null);
    }
}
