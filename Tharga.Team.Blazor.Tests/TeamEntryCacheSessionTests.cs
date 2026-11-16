using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// <c>TeamEntryCache.SessionOf</c> prefers the library's own session claim (Tharga/Team#471): it is what tells one person's browsers apart, issued by the server and
/// unique per sign-in, where the identity provider's <c>sid</c> may be shared or absent. Everything it did before still holds without the claim.
/// </summary>
public class TeamEntryCacheSessionTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "Test"));

    [Fact]
    public void TheLibrarysSessionClaim_IsPreferred_OverTheProvidersSid()
    {
        var principal = Principal(new Claim("sid", "provider-sid"), new Claim(TeamClaimTypes.SessionId, "library-session"), new Claim("auth_time", "1000"));

        Assert.Equal("library-session", TeamEntryCache.SessionOf(principal));
    }

    [Fact]
    public void TwoBrowsersOfOnePerson_WhoseProviderSidIsTheSame_AreToldApart_ByTheirSessions()
    {
        var one = Principal(new Claim("sid", "shared"), new Claim(TeamClaimTypes.SessionId, "session-1"));
        var two = Principal(new Claim("sid", "shared"), new Claim(TeamClaimTypes.SessionId, "session-2"));

        Assert.NotEqual(TeamEntryCache.SessionOf(one), TeamEntryCache.SessionOf(two));
    }

    [Fact]
    public void WithNoSessionClaim_TheSidStillTellsThemApart()
    {
        Assert.Equal("provider-sid", TeamEntryCache.SessionOf(Principal(new Claim("sid", "provider-sid"))));
    }

    [Fact]
    public void WithNeither_WhatTheSignInProved_StillTellsThemApart()
    {
        var bare = Principal(new Claim("auth_time", "1000"), new Claim("idp", "entra"), new Claim("amr", "pwd"));

        Assert.Equal("1000|entra|pwd", TeamEntryCache.SessionOf(bare));
    }

    [Fact]
    public void AnEmptySessionClaim_IsNotASession()
    {
        Assert.Equal("provider-sid", TeamEntryCache.SessionOf(Principal(new Claim("sid", "provider-sid"), new Claim(TeamClaimTypes.SessionId, ""))));
    }
}
