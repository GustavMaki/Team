using System.Runtime.CompilerServices;

// ITeamPrincipalAccessor moved to Tharga.Team, because TeamManagementService gates team reads from there
// and needs the caller's claims to do it. Reads previously recomputed the caller's scopes from their member
// row instead, which is what made consent-based access invisible to them (Tharga/Team#248).
//
// The namespace is unchanged, so no consumer's `using` breaks and a host's own ITeamPrincipalAccessor
// implementation still compiles. This forward keeps assemblies built against the old location loading, so
// an upgrade needs no recompilation either.
[assembly: TypeForwardedTo(typeof(Tharga.Team.Service.ITeamPrincipalAccessor))]

// TeamAccess moved to Tharga.Team for the same reason: the start-up checks in Tharga.Team.MongoDB read team data with no caller to authorize and have to
// say so, and that package does not reference this one. The namespace is unchanged and these forwards keep assemblies built against the old location
// loading, so an upgrade needs no recompilation (Tharga/Team#397).
[assembly: TypeForwardedTo(typeof(Tharga.Team.Service.TeamAccess))]
[assembly: TypeForwardedTo(typeof(Tharga.Team.Service.TeamAccessKind))]
[assembly: TypeForwardedTo(typeof(Tharga.Team.Service.TeamAccessContext))]
