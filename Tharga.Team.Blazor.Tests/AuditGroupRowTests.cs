using Tharga.Team.Blazor.Features.Audit;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The audit log draws a unit of work as one row, the entry that stands for it, and says when something inside it failed (Tharga/Team#402).
/// </summary>
public class AuditGroupRowTests
{
    private static readonly DateTime T0 = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);

    private static AuditEntry Entry(int seconds, AuditEventType type = AuditEventType.ServiceCall, bool success = true)
        => new() { Timestamp = T0.AddSeconds(seconds), EventType = type, Success = success };

    [Fact]
    public void AGroupOfSeveralEntries_IsDrawnAsItsLead_WithTheGroupBehindIt()
    {
        var change = Entry(1, AuditEventType.DataChange);
        var group = AuditEntryGroup.Of([Entry(0), change, Entry(2)]);
        var view = new AuditLogView();

        view.ShowGroups([group]);

        Assert.Same(group, view.GroupOf(change));
        Assert.Equal(3, view.GroupOf(change).Count);
    }

    /// <summary>A group of one is an entry: background work with no request trace looks as it did before.</summary>
    [Fact]
    public void AGroupOfOne_IsDrawnAsAPlainRow()
    {
        var only = Entry(0);
        var view = new AuditLogView();

        view.ShowGroups([AuditEntryGroup.Of([only])]);

        Assert.Null(view.GroupOf(only));
    }

    [Fact]
    public void ARowThatIsNotAGroup_HasNoGroup()
        => Assert.Null(new AuditLogView().GroupOf(Entry(0)));

    [Fact]
    public void TheRowsOwnFailure_IsTheOneShown()
    {
        var failed = Entry(0, AuditEventType.DataChange, success: false);
        var view = new AuditLogView();
        view.ShowGroups([AuditEntryGroup.Of([failed, Entry(1, success: false)])]);

        Assert.Same(failed, view.FailureOf(failed));
    }

    /// <summary>A request that failed somewhere inside is not shown as fine because its headline succeeded.</summary>
    [Fact]
    public void AFailureInsideTheGroup_IsShownOnItsRow_EvenWhenTheLeadSucceeded()
    {
        var lead = Entry(0, AuditEventType.DataChange);
        var failed = Entry(1, AuditEventType.ScopeDenial, success: false);
        var view = new AuditLogView();
        view.ShowGroups([AuditEntryGroup.Of([lead, failed])]);

        Assert.Same(failed, view.FailureOf(lead));
    }

    [Fact]
    public void ASuccessfulGroup_ShowsNoFailure()
    {
        var lead = Entry(0, AuditEventType.DataChange);
        var view = new AuditLogView();
        view.ShowGroups([AuditEntryGroup.Of([lead, Entry(1)])]);

        Assert.Null(view.FailureOf(lead));
    }

    [Fact]
    public void TwoEntriesEqualByValue_AreNotMixedUp()
    {
        var a = Entry(0, AuditEventType.DataChange);
        var b = a with { };
        var groupA = AuditEntryGroup.Of([a, Entry(1)]);
        var view = new AuditLogView();

        view.ShowGroups([groupA, AuditEntryGroup.Of([b])]);

        Assert.Same(groupA, view.GroupOf(a));
        Assert.Null(view.GroupOf(b));
    }
}
