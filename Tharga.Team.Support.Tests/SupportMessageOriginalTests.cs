using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A support mail is kept as it arrived, beside the case's files, and read back under the grants that read the case (Tharga/Team#499).
/// </summary>
/// <remarks>
/// <b>The original is never a file of the message.</b> It lives in the attachment store but is named only by the message's own
/// <see cref="SupportMessage.OriginalId"/>, so the attachment limits do not apply to it and it is never offered as an attachment.
/// </remarks>
public class SupportMessageOriginalTests
{
    private const string TeamA = "team-a";
    private const string CaseId = "case-1";
    private const string Correspondent = "user@example.com";
    private const string ThreadId = "opening-1@example.org";
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";
    private const string Staff = "staff-subject";

    private static readonly byte[] Raw = "From: user@example.com\r\nSubject: Re: Export is empty\r\n\r\nSee attached."u8.ToArray();

    [Fact]
    public async Task AReply_KeepsTheMailAsItArrived_AndTheMessageSaysSo()
    {
        var (handler, cases, files) = Build();

        await handler.HandleAsync(Reply());

        var reply = (await cases.GetMessagesAsync(TeamA, CaseId, null, 10)).Items.Single(x => x.Sequence == 2);
        Assert.True(reply.HasOriginal);
        var stored = Assert.Single(files.Files);
        Assert.Equal(CaseId, stored.CaseId);
        Assert.Equal(reply.OriginalId, stored.Attachment.Id);
        Assert.Equal("message/rfc822", stored.Attachment.ContentType);
        Assert.Equal(Raw, stored.Content);
    }

    [Fact]
    public async Task TheOriginal_IsNotListedAmongTheMessagesFiles()
    {
        var (handler, cases, _) = Build();

        await handler.HandleAsync(Reply());

        Assert.Empty((await cases.GetMessagesAsync(TeamA, CaseId, null, 10)).Items.Single(x => x.Sequence == 2).Attachments);
    }

    [Fact]
    public async Task ANewMail_KeepsItsOriginalUnderTheNewCase()
    {
        var (handler, cases, files) = Build();

        var outcome = await handler.HandleAsync(Reply() with { MessageId = "first-1@example.com", From = "stranger@example.com", InReplyTo = null, References = [] });

        Assert.True(outcome.OpenedCase);
        var first = (await cases.GetMessagesAsync(null, outcome.CaseId, null, 10)).Items.Single();
        Assert.Equal(Assert.Single(files.Files).Attachment.Id, first.OriginalId);
        Assert.Equal(outcome.CaseId, Assert.Single(files.Files).CaseId);
    }

    [Fact]
    public async Task AMailWithNoRaw_IsKeptWithoutAnOriginal()
    {
        var (handler, cases, files) = Build();

        await handler.HandleAsync(Reply() with { Raw = null });

        Assert.False((await cases.GetMessagesAsync(TeamA, CaseId, null, 10)).Items.Single(x => x.Sequence == 2).HasOriginal);
        Assert.Empty(files.Files);
    }

    /// <summary>The cap is its own, so a mail whose files fit may still be too large to keep whole; it is read as usual.</summary>
    [Fact]
    public async Task AnOriginalOverItsLimit_IsNotKept_AndTheMailStillReachesTheCase()
    {
        var (handler, cases, files) = Build(new MailOptions { MaxOriginalBytes = Raw.Length - 1 });

        var outcome = await handler.HandleAsync(Reply());

        Assert.True(outcome.WasApplied);
        Assert.False((await cases.GetMessagesAsync(TeamA, CaseId, null, 10)).Items.Single(x => x.Sequence == 2).HasOriginal);
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task AnOriginalAtItsLimit_IsKept()
    {
        var (handler, _, files) = Build(new MailOptions { MaxOriginalBytes = Raw.Length });

        await handler.HandleAsync(Reply());

        Assert.Single(files.Files);
    }

    [Fact]
    public async Task AStoreThatFails_LeavesTheMessageWithoutAnOriginal_AndTheMailStillReachesTheCase()
    {
        var (handler, cases, _) = Build(attachments: new InMemorySupportAttachmentStore { FailsFor = _ => true });

        var outcome = await handler.HandleAsync(Reply());

        Assert.True(outcome.WasApplied);
        Assert.False((await cases.GetMessagesAsync(TeamA, CaseId, null, 10)).Items.Single(x => x.Sequence == 2).HasOriginal);
    }

    [Fact]
    public async Task WithNoStore_TheMailIsKeptWithoutAnOriginal()
    {
        var (handler, cases, _) = Build(withStore: false);

        var outcome = await handler.HandleAsync(Reply());

        Assert.True(outcome.WasApplied);
        Assert.False((await cases.GetMessagesAsync(TeamA, CaseId, null, 10)).Items.Single(x => x.Sequence == 2).HasOriginal);
    }

    /// <summary>A mail the handler refuses leaves nothing behind, so a stranger cannot fill the store.</summary>
    [Fact]
    public async Task AMailFromSomeoneElse_KeepsNoOriginal()
    {
        var (handler, _, files) = Build();

        var outcome = await handler.HandleAsync(Reply() with { From = "stranger@example.com" });

        Assert.False(outcome.WasApplied);
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task TheAuthor_ReadsTheOriginalOfTheirOwnCase()
    {
        var (cases, files) = await CaseWithAnOriginalAsync();

        Assert.Equal(Raw, await Member(cases, files, Alice).GetMessageOriginalAsync(TeamA, CaseId, 2));
    }

    [Fact]
    public async Task ATeamSupportReader_ReadsTheOriginalOfSomebodyElsesCase()
    {
        var (cases, files) = await CaseWithAnOriginalAsync();

        Assert.Equal(Raw, await Member(cases, files, Bob, SupportScopes.Read).GetMessageOriginalAsync(TeamA, CaseId, 2));
    }

    [Fact]
    public async Task AMemberWithoutAGrant_IsRefused_ThoughTheyMayNotKnowThereIsOne()
    {
        var (cases, files) = await CaseWithAnOriginalAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Member(cases, files, Bob).GetMessageOriginalAsync(TeamA, CaseId, 2));
    }

    [Fact]
    public async Task AMemberOfAnotherTeam_IsRefused()
    {
        var (cases, files) = await CaseWithAnOriginalAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Build(cases, files, Bob, memberOfTeam: "team-b", teamScopes: [SupportScopes.Read]).GetMessageOriginalAsync(TeamA, CaseId, 2));
    }

    [Fact]
    public async Task CrossTeamStaff_ReadTheOriginal()
    {
        var (cases, files) = await CaseWithAnOriginalAsync();

        Assert.Equal(Raw, await Build(cases, files, Staff, systemScopes: [SystemSupportScopes.AllRead]).GetMessageOriginalAsync(TeamA, CaseId, 2));
    }

    [Fact]
    public async Task AMessageReceivedBeforeOriginalsWereKept_HasNone()
    {
        var (cases, files) = await CaseWithAnOriginalAsync();

        Assert.Null(await Member(cases, files, Alice).GetMessageOriginalAsync(TeamA, CaseId, 1));
    }

    [Fact]
    public async Task AnEntryThatDoesNotExist_HasNone()
    {
        var (cases, files) = await CaseWithAnOriginalAsync();

        Assert.Null(await Member(cases, files, Alice).GetMessageOriginalAsync(TeamA, CaseId, 9));
        Assert.Null(await Member(cases, files, Alice).GetMessageOriginalAsync(TeamA, CaseId, 0));
    }

    private static async Task<(InMemorySupportCaseStore Cases, InMemorySupportAttachmentStore Files)> CaseWithAnOriginalAsync()
    {
        var (handler, cases, files) = Build();
        await handler.HandleAsync(Reply());

        return (cases, files);
    }

    private static InboundMail Reply() => new(
        MessageId: "reply-1@example.com",
        From: Correspondent,
        DeliveredTo: ["support@example.org"],
        Subject: "Re: Export is empty",
        Body: "See attached.",
        SentAt: new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero),
        InReplyTo: ThreadId,
        References: [ThreadId],
        Raw: Raw);

    private static (EmailEventHandler Handler, InMemorySupportCaseStore Cases, InMemorySupportAttachmentStore Files) Build(
        MailOptions options = null,
        InMemorySupportAttachmentStore attachments = null,
        bool withStore = true)
    {
        var cases = new InMemorySupportCaseStore();
        cases.AddCaseAsync(new SupportCase
        {
            Id = CaseId,
            TeamKey = TeamA,
            AuthorIdentity = Alice,
            AuthorName = "A User",
            Subject = "Export is empty",
            Status = SupportCaseStatus.Open,
            CreatedAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc),
            MessageCount = 1,
            Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = ThreadId, Address = Correspondent }]
        }, new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorIdentity = Alice,
            AuthorName = "A User",
            Body = "It is empty.",
            SentAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc)
        }).GetAwaiter().GetResult();

        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var mailOptions = options ?? new MailOptions();
        mailOptions.FromAddress = "support@example.org";

        var files = attachments ?? new InMemorySupportAttachmentStore();

        return (new EmailEventHandler(cases, ledger, Options.Create(mailOptions), TimeProvider.System, attachmentStore: withStore ? files : null), cases, files);
    }

    private static ISupportCaseService Member(InMemorySupportCaseStore cases, InMemorySupportAttachmentStore files, string subject, string teamScope = null)
        => Build(cases, files, subject, memberOfTeam: TeamA, teamScopes: teamScope == null ? [] : [teamScope]);

    private static ISupportCaseService Build(
        ISupportCaseStore cases,
        InMemorySupportAttachmentStore files,
        string subject,
        string memberOfTeam = null,
        string[] teamScopes = null,
        string[] systemScopes = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject)
        };

        if (memberOfTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));
        claims.AddRange((teamScopes ?? []).Select(s => new Claim(TeamClaimTypes.Scope, s)));
        claims.AddRange((systemScopes ?? []).Select(s => new Claim(TeamClaimTypes.SystemScope, s)));

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(cases, authorizer, TimeProvider.System, attachmentStore: files),
            authorizer,
            SupportCaseVisibilityMode.TeamWide);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
