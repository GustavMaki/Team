using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// The link an invitee follows. Used by the invitation email and by "Copy invitation link" alike: one builder, so
/// <see cref="ThargaBlazorOptions.InvitePath"/> reaches both.
/// </summary>
/// <remarks>
/// The clipboard half is why the option exists rather than leaving hosts to rewrite the link themselves: an
/// <c>ITeamEmailSender</c> can rewrite what it sends, but nothing can rewrite what an administrator copies out of the page.
/// </remarks>
internal static class TeamInviteLink
{
    /// <summary>The code alone, and the team key stays out of the link entirely (Tharga/Team#249): resolving the code to its team is the store's job.</summary>
    public static string Build(string baseUri, string invitePath, string inviteKey)
    {
        var path = InvitePathResolver.Resolve(invitePath);
        return $"{baseUri}{path}?{Constants.TeamInviteToken}={Uri.EscapeDataString(inviteKey)}";
    }
}
