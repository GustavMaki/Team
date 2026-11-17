using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.Service.Audit;

public interface IAuditRepositoryCollection : IDiskRepositoryCollection<AuditEntryEntity>
{
    /// <summary>
    /// The entries matching <paramref name="filter"/>, folded by the server into one group per correlation id and paged by group.
    /// </summary>
    /// <returns>
    /// The page of groups, each in the order its entries were written, ordered by when each began; and how many groups match in all.
    /// </returns>
    /// <remarks>An entry with no correlation id (written before 3.21) is a group of its own.</remarks>
    Task<(IReadOnlyList<IReadOnlyList<AuditEntryEntity>> Groups, int Total)> GetGroupsAsync(FilterDefinition<AuditEntryEntity> filter, int skip, int take, bool newestFirst, CancellationToken cancellationToken = default);
}