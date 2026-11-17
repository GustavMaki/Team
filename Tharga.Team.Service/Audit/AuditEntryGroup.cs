namespace Tharga.Team.Service.Audit;

/// <summary>
/// The matching audit entries of one unit of work: everything written under one <see cref="AuditEntry.CorrelationId"/>.
/// </summary>
/// <remarks>
/// One user action can write many entries — the domain entry, the access trace of the call, and the traces of the page
/// reloading what it shows. A group lets a reader see the action once and open it for the rest.
/// <para>
/// Background work with no request has a correlation id of its own, so its entries group the same way; a group of one
/// entry is simply an entry.
/// </para>
/// </remarks>
public record AuditEntryGroup
{
    /// <summary>The entries of the group in the order they were written. Never empty.</summary>
    public IReadOnlyList<AuditEntry> Entries { get; init; } = Array.Empty<AuditEntry>();

    /// <summary>The entry that stands for the group: its first <see cref="AuditEventType.DataChange"/>, else its first entry.</summary>
    public AuditEntry Lead => Entries.FirstOrDefault(x => x.EventType == AuditEventType.DataChange) ?? Entries.FirstOrDefault();

    /// <summary>The entries of the group other than <see cref="Lead"/>, with the access traces last.</summary>
    public IReadOnlyList<AuditEntry> Others => [.. Entries
        .Where(x => !ReferenceEquals(x, Lead))
        .OrderBy(x => x.EventType == AuditEventType.DataChange ? 0 : 1)];

    /// <summary>How many entries the group holds.</summary>
    public int Count => Entries.Count;

    /// <summary>When the unit of work began: its first entry.</summary>
    public DateTime Started => Entries.Count == 0 ? default : Entries.Min(x => x.Timestamp);

    /// <summary>Whether any entry of the group failed.</summary>
    public bool Failed => Entries.Any(x => !x.Success);

    /// <summary>A group made of these entries, in the order they were written.</summary>
    public static AuditEntryGroup Of(IEnumerable<AuditEntry> entries) => new() { Entries = [.. entries.OrderBy(x => x.Timestamp)] };
}

/// <summary>A page of <see cref="AuditEntryGroup"/>, newest unit of work first unless the query sorts the other way.</summary>
public record AuditGroupResult
{
    public IReadOnlyList<AuditEntryGroup> Items { get; init; } = Array.Empty<AuditEntryGroup>();

    /// <summary>How many groups match the query, not how many entries.</summary>
    public int TotalCount { get; init; }
}
