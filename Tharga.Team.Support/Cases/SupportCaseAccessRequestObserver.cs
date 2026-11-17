using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Shows a request for access to a team in the support case it was made from: that it was asked for, and how it was decided.
/// </summary>
/// <remarks>
/// <b>Both sides read it where the conversation is.</b> The customer sees that support asked, and support sees when access was granted and
/// until when, as <see cref="SupportMessageKind.System"/> entries in the transcript.
/// <para>
/// <b>Only a request that names a case of that team is shown.</b> The case is looked up under the team the request is for, so a request quoting
/// a case of another team finds nothing and posts nothing. The text is fixed and says only what the request itself says, so quoting a case id
/// writes nothing a requester chose.
/// </para>
/// <para>
/// <b>Never in the way of the request.</b> The service that tells it has already made the change; a failure here is logged by that service and
/// leaves the request as it was decided.
/// </para>
/// </remarks>
internal sealed class SupportCaseAccessRequestObserver(
    ISupportCaseStore store,
    ISupportCaseNotifier notifier,
    TimeProvider timeProvider,
    ILogger<SupportCaseAccessRequestObserver> logger = null) : ITeamAccessRequestObserver
{
    public async Task ChangedAsync(string teamKey, TeamAccessRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request?.SupportCaseId) || string.IsNullOrWhiteSpace(teamKey)) return;

        var supportCase = await store.GetCaseAsync(teamKey, request.SupportCaseId, cancellationToken);

        if (supportCase == null)
        {
            logger?.LogDebug("Access request {RequestId} names support case {CaseId}, which is not a case of team {TeamKey}, so nothing was posted.", request.Id, request.SupportCaseId, teamKey);
            return;
        }

        await store.AppendMessageAsync(teamKey, supportCase.Id, new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.System,
            AuthorName = request.Status == TeamAccessRequestStatus.Pending || request.Status == TeamAccessRequestStatus.Cancelled ? request.RequesterName : null,
            Body = Describe(request),
            SentAt = timeProvider.GetUtcNow().UtcDateTime
        }, cancellationToken);

        notifier?.Notify(new SupportCaseUpdatedEventArgs
        {
            TeamKey = teamKey,
            CaseId = supportCase.Id,
            Change = SupportCaseChange.Replied,
            FromChannel = false
        });
    }

    /// <summary>What the transcript says about the request, in the words of the other system entries.</summary>
    internal static string Describe(TeamAccessRequest request)
    {
        var who = string.IsNullOrWhiteSpace(request.RequesterName) ? "Support" : request.RequesterName;

        return request.Status switch
        {
            TeamAccessRequestStatus.Pending => $"{who} asked for {request.AccessLevel} access to the team {Duration(request.Duration)}.",
            TeamAccessRequestStatus.Approved => $"Access to the team was granted at {request.AccessLevel}{Until(request.GrantedUntil)}.",
            TeamAccessRequestStatus.Denied => "The request for access to the team was denied.",
            _ => $"{who} withdrew the request for access to the team."
        };
    }

    private static string Duration(TimeSpan? duration)
    {
        if (duration is not { } length) return "with no end";

        if (length.TotalDays >= 1 && length.TotalDays % 1 == 0)
            return length.TotalDays == 1 ? "for 1 day" : $"for {(int)length.TotalDays} days";

        var hours = Math.Max(1, (int)Math.Round(length.TotalHours));

        return hours == 1 ? "for 1 hour" : $"for {hours} hours";
    }

    private static string Until(DateTime? grantedUntil)
        => grantedUntil is { } until ? $" until {until.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC" : " with no end";
}
