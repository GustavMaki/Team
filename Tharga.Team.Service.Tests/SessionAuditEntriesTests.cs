using System.Security.Claims;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The audit entries for the session's own events (Tharga/Team#471), beside <c>AuthAuditEntriesTests</c>: a sign-out, a session ended by a limit or by the
/// person, and one ended because a newer sign-in took the team. All are feature <c>auth</c>, name the person and never the session id, which is a key.
/// </summary>
public class SessionAuditEntriesTests
{
    private const string SessionId = "session-id-that-must-not-be-logged";

    private static ClaimsPrincipal Caller(params Claim[] claims)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "subject-1"), new Claim(ClaimTypes.Email, "alice@example.com"), new Claim(TeamClaimTypes.SessionId, SessionId), .. claims], "test"));

    private static void AssertNoSessionId(AuditEntry entry)
    {
        Assert.DoesNotContain(SessionId, entry.Metadata?.Values.ToArray() ?? []);
        Assert.DoesNotContain(SessionId, entry.CallerIdentity ?? string.Empty);
        Assert.DoesNotContain(SessionId, entry.CallerUserIdentity ?? string.Empty);
    }

    [Fact]
    public void SignOut_IsAnAuthEntry_ForAUserOnTheWeb_NamingThem()
    {
        var entry = AuthAuditEntries.SignOut(Caller());

        Assert.Equal("auth", entry.Feature);
        Assert.Equal("signout", entry.Action);
        Assert.True(entry.Success);
        Assert.Equal(AuditCallerType.User, entry.CallerType);
        Assert.Equal(AuditCallerSource.Web, entry.CallerSource);
        Assert.Equal("alice@example.com", entry.CallerIdentity);
        Assert.Equal("subject-1", entry.CallerUserIdentity);
        Assert.Null(entry.TeamKey);
        AssertNoSessionId(entry);
    }

    [Fact]
    public void SignOut_ReadsTheSubject_WithTheHostsResolver()
    {
        var entry = AuthAuditEntries.SignOut(Caller(new Claim("oid", "object-9")), new UserIdentityResolver(["oid"]));

        Assert.Equal("object-9", entry.CallerUserIdentity);
    }

    [Fact]
    public void SignOut_WithNoPrincipal_StillProducesAnEntry()
    {
        Assert.Equal("signout", AuthAuditEntries.SignOut(null).Action);
    }

    [Theory]
    [InlineData(SessionEndReason.MaxAge)]
    [InlineData(SessionEndReason.Idle)]
    [InlineData(SessionEndReason.NewerSignIn)]
    [InlineData(SessionEndReason.Ended)]
    [InlineData(SessionEndReason.SignedOut)]
    public void SessionEnded_NamesTheReason(SessionEndReason reason)
    {
        var entry = AuthAuditEntries.SessionEnded(Caller(), reason);

        Assert.Equal(("auth", "session-ended"), (entry.Feature, entry.Action));
        Assert.Contains(reason.ToString(), entry.Metadata.Values);
        Assert.Equal("alice@example.com", entry.CallerIdentity);
        Assert.Equal("subject-1", entry.CallerUserIdentity);
        AssertNoSessionId(entry);
    }

    [Fact]
    public void SessionEnded_WithNoPrincipal_StillProducesAnEntry()
    {
        Assert.Equal("session-ended", AuthAuditEntries.SessionEnded(null, SessionEndReason.Idle).Action);
    }

    [Fact]
    public void SessionSuperseded_NamesThePersonAndTheTeam_NotTheSession()
    {
        var superseded = new UserSession { SessionId = SessionId, UserKey = "user-1", Subject = "subject-1", SignedInAt = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc), Origin = SessionOrigin.SignIn, ActiveTeamKey = "acme" };

        var entry = AuthAuditEntries.SessionSuperseded(superseded);

        Assert.Equal(("auth", "session-superseded"), (entry.Feature, entry.Action));
        Assert.Equal("subject-1", entry.CallerUserIdentity);
        Assert.Equal("acme", entry.TeamKey);
        AssertNoSessionId(entry);
    }

    [Fact]
    public void TheThreeActions_AreDistinct()
    {
        Assert.Equal(3, new[] { AuthAuditEntries.SignOutAction, AuthAuditEntries.SessionEndedAction, AuthAuditEntries.SessionSupersededAction }.Distinct().Count());
        Assert.DoesNotContain(AuthAuditEntries.SignInAction, new[] { AuthAuditEntries.SignOutAction, AuthAuditEntries.SessionEndedAction, AuthAuditEntries.SessionSupersededAction });
    }
}
