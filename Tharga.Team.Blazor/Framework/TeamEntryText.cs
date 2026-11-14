namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered when the host's team entry guard refuses a team: <c>TeamEntryNotice</c> and the team selector.</summary>
public static class TeamEntryText
{
    /// <summary>Heading of the notice.</summary>
    public static readonly TextKey Title = new("team.entry.title", "You cannot work in this team yet");

    /// <summary>Shown when the guard gave no reason of its own.</summary>
    public static readonly TextKey DefaultReason = new("team.entry.reason", "Entry to this team is not allowed right now.");

    /// <summary>The button that sends the caller to do what the guard asks, such as signing in more strongly.</summary>
    public static readonly TextKey Continue = new("team.entry.continue", "Continue");

    /// <summary>The badge beside the selector when the chosen team was refused.</summary>
    public static readonly TextKey Badge = new("team.entry.badge", "Not allowed");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Title, DefaultReason, Continue, Badge];
}
