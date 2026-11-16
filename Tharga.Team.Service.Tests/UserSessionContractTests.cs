using System.Security.Claims;
using System.Text.Json;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The session's contract types (Tharga/Team#471): they cross the store port and the wire, so they serialize by construction, and the claim that carries the
/// session is not the identity provider's.
/// </summary>
public class UserSessionContractTests
{
    private static readonly DateTime T0 = new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ASession_SurvivesAJsonRoundTrip()
    {
        var session = new UserSession
        {
            SessionId = "s1",
            UserKey = "user-1",
            Subject = "subject-1",
            SignedInAt = T0,
            IdentityProvider = "entra",
            AuthenticationMethods = ["pwd", "mfa"],
            Origin = SessionOrigin.Adopted,
            LastActivityAt = T0.AddMinutes(3),
            ActiveTeamKey = "acme",
            EndedAt = T0.AddHours(1),
            EndReason = SessionEndReason.NewerSignIn
        };

        var read = JsonSerializer.Deserialize<UserSession>(JsonSerializer.Serialize(session));

        Assert.NotNull(read);
        Assert.Equal((session.SessionId, session.UserKey, session.Subject, session.SignedInAt, session.IdentityProvider), (read.SessionId, read.UserKey, read.Subject, read.SignedInAt, read.IdentityProvider));
        Assert.Equal(session.AuthenticationMethods, read.AuthenticationMethods);
        Assert.Equal((session.Origin, session.LastActivityAt, session.ActiveTeamKey, session.EndedAt, session.EndReason), (read.Origin, read.LastActivityAt, read.ActiveTeamKey, read.EndedAt, read.EndReason));
    }

    [Fact]
    public void ASessionStillGoing_RoundTripsWithNoEnd()
    {
        var session = new UserSession { SessionId = "s1", UserKey = "user-1", SignedInAt = T0, Origin = SessionOrigin.SignIn };

        var read = JsonSerializer.Deserialize<UserSession>(JsonSerializer.Serialize(session));

        Assert.Null(read.EndedAt);
        Assert.Null(read.EndReason);
        Assert.Null(read.ActiveTeamKey);
    }

    [Fact]
    public void TheLimits_SurviveAJsonRoundTrip()
    {
        var limits = new SessionLimits { MaxAge = TimeSpan.FromHours(8), IdleTimeout = TimeSpan.FromMinutes(2), SingleSession = true };

        Assert.Equal(limits, JsonSerializer.Deserialize<SessionLimits>(JsonSerializer.Serialize(limits)));
    }

    [Fact]
    public void NoLimits_AreNoLimits()
    {
        Assert.Null(SessionLimits.None.MaxAge);
        Assert.Null(SessionLimits.None.IdleTimeout);
        Assert.False(SessionLimits.None.SingleSession);
    }

    [Fact]
    public void TheSessionClaim_IsNotTheIdentityProvidersSid()
    {
        Assert.NotEqual("sid", TeamClaimTypes.SessionId);
        Assert.NotEqual(ClaimTypes.Sid, TeamClaimTypes.SessionId);
        Assert.NotEmpty(TeamClaimTypes.SessionId);
    }

    [Fact]
    public void TheReasons_AreTheFiveTheDesignNames()
    {
        Assert.Equal(["Ended", "Idle", "MaxAge", "NewerSignIn", "SignedOut"], Enum.GetNames<SessionEndReason>().Order());
        Assert.Equal(["Adopted", "Host", "SignIn"], Enum.GetNames<SessionOrigin>().Order());
    }
}
