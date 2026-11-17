namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings of the access a support case shows for its team: what the staff member already holds, and the reason a request starts with.
/// </summary>
public static class SupportCaseAccessText
{
    /// <summary>Under a case, to the staff member who already has access to its team: <c>{0}</c> is the level and <c>{1}</c> how they hold it.</summary>
    public static readonly TextKey YourAccess = new("team.support.caseAccess.yourAccess", "Your access to this team: {0} ({1})");

    /// <summary>How the access is held, when it comes from being a member of the team.</summary>
    public static readonly TextKey AsMember = new("team.support.caseAccess.asMember", "as a member");

    /// <summary>How the access is held, when it comes from the team's consent to a role the staff member holds.</summary>
    public static readonly TextKey ThroughConsent = new("team.support.caseAccess.throughConsent", "through the team's consent");

    /// <summary>What the reason of a request made from a case starts as: <c>{0}</c> is the case's subject.</summary>
    public static readonly TextKey CaseReason = new("team.support.caseAccess.caseReason", "Support case: {0}");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [YourAccess, AsMember, ThroughConsent, CaseReason];
}
