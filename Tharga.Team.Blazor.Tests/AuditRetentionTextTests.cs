using Tharga.Team.Blazor.Features.Audit;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The audit view states how long entries are kept, so a short history reads as retention rather than as
/// nothing having happened. Unlimited retention has to say so explicitly — silence would leave the reader
/// to guess which of the two they are looking at.
/// </summary>
public class AuditRetentionTextTests
{
    [Theory]
    [InlineData(90)]
    [InlineData(30)]
    [InlineData(1)]
    public void WithRetention_NamesTheNumberOfDays(int days)
    {
        Assert.Equal($"Entries are kept for {days} days, then deleted automatically.", AuditRetentionText.Describe(days, TextSet.Empty));
    }

    [Theory]
    // null, 0 and negatives all mean "no TTL index" to AuditOptions — they must read the same way.
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void WithoutRetention_SaysEntriesAreKept(int? days)
    {
        Assert.Equal("Entries are kept indefinitely.", AuditRetentionText.Describe(days, TextSet.Empty));
    }

    [Fact]
    public void ATranslation_ReplacesBothSentences_WithTheDaysWhereTheTranslatorPutThem()
    {
        var text = new TextSet(new Dictionary<string, string>
        {
            [AuditLogViewText.RetentionDays.Key] = "Posterna sparas i {0} dagar och raderas sedan automatiskt.",
            [AuditLogViewText.RetentionIndefinite.Key] = "Posterna sparas utan tidsgräns."
        });

        Assert.Equal("Posterna sparas i 427 dagar och raderas sedan automatiskt.", AuditRetentionText.Describe(427, text));
        Assert.Equal("Posterna sparas utan tidsgräns.", AuditRetentionText.Describe(null, text));
    }
}
