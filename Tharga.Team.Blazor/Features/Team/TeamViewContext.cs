using System.ComponentModel;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;
using Tharga.Toolkit;

namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// What the team components read once and share, so a page of several teams reads the caller, the users and the registries
/// once and not once per team. Opaque: <see cref="TeamComponent{TMember}"/> builds it and hands it to the components it is made of.
/// </summary>
/// <remarks>
/// Public only because a component parameter has to be, and never needed by a host: <see cref="TeamMembersView{TMember}"/> loads
/// its own when it is used alone.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class TeamViewContext
{
    private static readonly IReadOnlySet<string> NoHiddenScopes = new HashSet<string>();

    private readonly Dictionary<string, IReadOnlyList<TenantRoleDefinition>> _visibleRolesByTeam = new();
    private readonly Dictionary<string, IReadOnlySet<string>> _hiddenScopesByTeam = new();

    internal TeamViewContext()
    {
    }

    internal TextSet Text { get; private set; } = TextSet.Empty;
    internal TextSet RequestText { get; private set; } = TextSet.Empty;

    internal IUser User { get; private set; }
    internal IUser[] Users { get; private set; } = [];
    internal ITeam SelectedTeam { get; private set; }

    internal bool HasManageScope { get; private set; }
    internal bool HasMemberManageScope { get; private set; }
    internal bool HasSystemAuditRead { get; private set; }
    internal bool HasTeamAuditRead { get; private set; }
    internal bool CanSeeAllTeams { get; private set; }
    internal bool AllowTeamCreation { get; private set; }
    internal string CreateTeamPath { get; private set; }
    internal bool ShowConsentToggle { get; private set; }
    internal bool CanSeeConsent { get; private set; }
    internal string[] ConsentRoles { get; private set; } = [];
    internal string[] CallerRoles { get; private set; } = [];
    internal AccessLevel ConsentDefaultLevel { get; private set; }

    // Both narrowed by ThargaBlazorOptions.HiddenAccessLevels. Defaulted to the full sets so a render that beats
    // loading offers the built-in lists rather than nothing.
    internal AccessLevel[] MemberAccessLevels { get; private set; } = AccessLevelVisibility.Member;
    internal AccessLevel[] ConsentLevels { get; private set; } = AccessLevelVisibility.Consent;

    internal IReadOnlyList<TeamAccessRequestItem> MyAccessRequests { get; private set; } = [];

    internal IScopeRegistry ScopeRegistry { get; private set; }
    internal ITenantRoleRegistry TenantRoleRegistry { get; private set; }
    internal ITenantRoleService TenantRoleService { get; private set; }
    internal ITenantRoleVisibilityProvider RoleVisibilityProvider { get; private set; }
    internal ITeamScopeVisibilityProvider ScopeVisibilityProvider { get; private set; }
    internal IReadOnlyList<TenantRoleDefinition> RoleDefinitions { get; private set; } = Array.Empty<TenantRoleDefinition>();
    internal string[] AllScopeNames { get; private set; } = [];
    internal IReadOnlyDictionary<string, string> ScopeDescriptions { get; private set; } = new Dictionary<string, string>();

    /// <summary>Reads the caller, the users, the registries and the options. Teams are read separately, by whoever shows them.</summary>
    internal static async Task<TeamViewContext> LoadAsync(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<ThargaBlazorOptions>>().Value;
        var userService = services.GetRequiredService<IUserService>();
        var textProvider = services.GetRequiredService<IThargaTextProvider>();

        var context = new TeamViewContext
        {
            Text = await textProvider.ResolveAsync(TeamComponentText.All),
            RequestText = await textProvider.ResolveAsync(TeamAccessRequestText.All)
        };

        var authState = await services.GetRequiredService<AuthenticationStateProvider>().GetAuthenticationStateAsync();
        var claims = authState.User;
        context.HasManageScope = claims.HasClaim(TeamClaimTypes.Scope, TeamScopes.Manage);
        context.HasMemberManageScope = claims.HasClaim(TeamClaimTypes.Scope, TeamScopes.MemberManage);
        context.HasSystemAuditRead = TeamScopeGate.HasSystemScope(claims, AuditScopes.Read);
        context.HasTeamAuditRead = claims.HasClaim(TeamClaimTypes.Scope, AuditScopes.Read);
        context.CanSeeAllTeams = TeamVisibility.CanSeeAllTeams(claims);
        context.CallerRoles = [.. claims.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value)];

        context.ScopeRegistry = services.GetService<IScopeRegistry>();
        context.TenantRoleRegistry = services.GetService<ITenantRoleRegistry>();
        context.TenantRoleService = services.GetService<ITenantRoleService>();
        context.RoleVisibilityProvider = services.GetService<ITenantRoleVisibilityProvider>();
        context.ScopeVisibilityProvider = services.GetService<ITeamScopeVisibilityProvider>();

        context.AllowTeamCreation = options.AllowTeamCreation;
        context.CreateTeamPath = options.CreateTeamPath;
        context.ShowConsentToggle = options.Consent.ShowToggle;
        context.ConsentRoles = options.Consent.Roles ?? [];
        context.CanSeeConsent = TeamVisibility.CanSeeConsent(context.CanSeeAllTeams, context.CallerRoles, context.ConsentRoles);
        context.ConsentDefaultLevel = options.Consent.AccessLevel;
        context.MemberAccessLevels = AccessLevelVisibility.Apply(AccessLevelVisibility.Member, options.HiddenAccessLevels);
        context.ConsentLevels = AccessLevelVisibility.Apply(AccessLevelVisibility.Consent, options.HiddenAccessLevels);
        if (context.TenantRoleRegistry != null) context.RoleDefinitions = context.TenantRoleRegistry.All;
        if (context.ScopeRegistry != null)
        {
            context.AllScopeNames = context.ScopeRegistry.All.Where(s => !s.GrantOnly).Select(s => s.Name).ToArray();
            context.ScopeDescriptions = context.ScopeRegistry.All.ToDictionary(s => s.Name, s => s.Description);
        }

        context.User = await userService.GetCurrentUserAsync();
        context.Users = UserDirectoryGate.Resolve(TeamScopeGate.HasSystemScope(claims, SystemUserScopes.Manage)) == UserDirectorySource.FullDirectory
            ? await userService.GetAsync().ToArrayAsync()
            : [.. await userService.GetTeamMemberUsersAsync()];

        return context;
    }

    /// <summary>The team the caller has selected, which the per-team scopes were issued for.</summary>
    internal async Task RefreshSelectedAsync(IServiceProvider services)
        => SelectedTeam = await services.GetRequiredService<ITeamStateService>().GetSelectedTeamAsync();

    /// <summary>What depends on the teams that were read: which roles and scopes each may show, and the caller's own access requests.</summary>
    internal async Task RefreshTeamsAsync(IServiceProvider services, IReadOnlyCollection<ITeam> teams)
    {
        await BuildVisibleRolesAsync(teams);
        await BuildHiddenScopesAsync(teams);

        // Only a caller who can request somewhere has requests to find, and finding them reads every team.
        MyAccessRequests = CanSeeAllTeams && TeamAccessRequestGate.CanRequest(false, CallerRoles, ConsentRoles)
            ? await services.GetRequiredService<ITeamAccessRequestService>().GetMyAccessRequestsAsync()
            : [];
    }

    private async Task BuildVisibleRolesAsync(IReadOnlyCollection<ITeam> teams)
    {
        _visibleRolesByTeam.Clear();

        foreach (var team in teams)
        {
            var teamRoles = RolesForTeam(team);
            if (teamRoles.Count == 0 || RoleVisibilityProvider == null)
            {
                _visibleRolesByTeam[team.Key] = teamRoles;
                continue;
            }

            var visible = new List<TenantRoleDefinition>(teamRoles.Count);
            foreach (var role in teamRoles)
            {
                if (await RoleVisibilityProvider.IsRoleVisibleAsync(team.Key, role.Name)) visible.Add(role);
            }
            _visibleRolesByTeam[team.Key] = visible;
        }
    }

    // Code-registered roles plus (when dynamic roles are enabled) the team's own custom roles. Code roles win on a
    // name clash. Reads the team's custom roles from the already-loaded team document (no extra fetch).
    private IReadOnlyList<TenantRoleDefinition> RolesForTeam(ITeam team)
    {
        if (TenantRoleService == null || team.CustomRoles is not { Count: > 0 }) return RoleDefinitions;

        var codeNames = RoleDefinitions.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        return RoleDefinitions
            .Concat(team.CustomRoles.Where(r => !codeNames.Contains(r.Name)))
            .ToList();
    }

    // The scope counterpart of BuildVisibleRolesAsync: per team, the registered scopes its ITeamScopeVisibilityProvider
    // hides. The pickers leave those out and keep any already held.
    private async Task BuildHiddenScopesAsync(IReadOnlyCollection<ITeam> teams)
    {
        _hiddenScopesByTeam.Clear();
        if (ScopeRegistry == null || ScopeVisibilityProvider == null) return;

        var scopes = ScopeRegistry.All.Select(s => s.Name).ToArray();
        foreach (var team in teams)
        {
            _hiddenScopesByTeam[team.Key] = await ScopeVisibility.HiddenAsync(ScopeVisibilityProvider, team.Key, scopes);
        }
    }

    internal IReadOnlyList<TenantRoleDefinition> GetVisibleRoles(string teamKey)
        => _visibleRolesByTeam.TryGetValue(teamKey, out var roles) ? roles : RoleDefinitions;

    internal IReadOnlySet<string> GetHiddenScopes(string teamKey)
        => _hiddenScopesByTeam.TryGetValue(teamKey, out var hidden) ? hidden : NoHiddenScopes;

    internal bool IsMemberOf<TMember>(ITeam<TMember> team) where TMember : class, ITeamMember
        => team.Members.Any(x => x.Key == User.Key && x.State == MembershipState.Member);

    internal bool HasAccessLevel<TMember>(ITeam<TMember> team, AccessLevel accessLevel) where TMember : class, ITeamMember
    {
        var member = team.Members.FirstOrDefault(x => x.Key == User.Key);
        // Explicit rather than left to the lifted <=, which is false for null by accident: an unset level is below every
        // level, and before 4.0 it read as Owner.
        if (member?.AccessLevel is not { } level) return false;
        return level <= accessLevel;
    }

    /// <summary>
    /// The level the caller actually holds on <paramref name="team"/>: their membership, or the team's consent when they are
    /// not a member, or null for neither, the same answer the selected team's claims carry, so this column and the top bar
    /// cannot disagree. A suspended member holds nothing and does not fall through to consent, matching <c>TeamGrantResolver</c>.
    /// </summary>
    internal AccessLevel? MyAccessLevel<TMember>(ITeam<TMember> team) where TMember : class, ITeamMember
    {
        var member = team.Members?.FirstOrDefault(x => x.Key == User?.Key && x.State == MembershipState.Member);
        if (member != null) return member.SuspendedAt == null ? member.AccessLevel : null;

        return TeamGrantResolver.ConsentLevel(team, CallerRoles, ConsentDefaultLevel, DateTime.UtcNow);
    }

    internal AccessLevel? ConsentOf(ITeam team) => TeamVisibility.Resolve(team, ConsentDefaultLevel, DateTime.UtcNow);

    internal bool CanRenameTeam(ITeam team) => TeamActionGate.CanRename(HasManageScope, SelectedTeam?.Key, team.Key);

    internal bool CanManageMembersOn(ITeam team) => TeamActionGate.CanManageMembers(HasMemberManageScope, SelectedTeam?.Key, team.Key);

    internal bool CanReadMemberAudit(ITeam team) => TeamActionGate.CanReadMemberAudit(HasSystemAuditRead, HasTeamAuditRead, SelectedTeam?.Key, team.Key);

    internal bool CanDeleteTeam<TMember>(ITeam<TMember> team) where TMember : class, ITeamMember
        => TeamActionGate.CanDelete(HasManageScope, SelectedTeam?.Key, team.Key, AllowTeamCreation, HasAccessLevel(team, AccessLevel.Owner));

    internal bool CanLeaveTeam<TMember>(ITeam<TMember> team) where TMember : class, ITeamMember
        => TeamActionGate.CanLeave(IsMemberOf(team), HasAccessLevel(team, AccessLevel.Owner));

    internal bool CanTransferOwnership<TMember>(ITeam<TMember> team) where TMember : class, ITeamMember
        => TeamActionGate.CanTransferOwnership(HasAccessLevel(team, AccessLevel.Owner), team.Members.Length > 1, SelectedTeam?.Key, team.Key);

    internal bool CanEditConsent<TMember>(ITeam<TMember> team) where TMember : class, ITeamMember
        => TeamActionGate.CanEditConsent(HasManageScope, SelectedTeam?.Key, team.Key, HasAccessLevel(team, AccessLevel.Administrator));
}
