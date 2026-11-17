namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportAssistantUsageView</c>: how much the support assistant was used, per team,
/// provider and model, for the people who run or bill the product.
/// </summary>
public static class SupportAssistantUsageViewText
{
    /// <summary>The start of the period, inclusive.</summary>
    public static readonly TextKey From = new("team.support.usage.from", "From");

    /// <summary>The end of the period, inclusive.</summary>
    public static readonly TextKey To = new("team.support.usage.to", "To");

    /// <summary>The team column.</summary>
    public static readonly TextKey Team = new("team.support.usage.team", "Team");

    /// <summary>The provider column.</summary>
    public static readonly TextKey Provider = new("team.support.usage.provider", "Provider");

    /// <summary>The model column.</summary>
    public static readonly TextKey Model = new("team.support.usage.model", "Model");

    /// <summary>The column counting answers.</summary>
    public static readonly TextKey Answers = new("team.support.usage.answers", "Answers");

    /// <summary>The column counting declines, which spent tokens too.</summary>
    public static readonly TextKey Declines = new("team.support.usage.declines", "Declined");

    /// <summary>The column counting the assistant's service calls.</summary>
    public static readonly TextKey ToolCalls = new("team.support.usage.toolCalls", "Service calls");

    /// <summary>The column of tokens the model read.</summary>
    public static readonly TextKey InputTokens = new("team.support.usage.inputTokens", "Input tokens");

    /// <summary>The column of input tokens read from the provider's cache.</summary>
    public static readonly TextKey CachedTokens = new("team.support.usage.cachedTokens", "Of which cached");

    /// <summary>The column of tokens the model wrote.</summary>
    public static readonly TextKey OutputTokens = new("team.support.usage.outputTokens", "Output tokens");

    /// <summary>The column of all tokens.</summary>
    public static readonly TextKey TotalTokens = new("team.support.usage.totalTokens", "Total tokens");

    /// <summary>The column of what the answers cost.</summary>
    public static readonly TextKey Cost = new("team.support.usage.cost", "Cost");

    /// <summary>The line above the table adding up the period: answers, tokens in (cached) and out.</summary>
    public static readonly TextKey TotalsLine = new("team.support.usage.totalsLine", "{0:N0} answers, {1:N0} tokens: {2:N0} in ({3:N0} cached) and {4:N0} out.");

    /// <summary>Added to the totals line when answers were priced.</summary>
    public static readonly TextKey CostLine = new("team.support.usage.costLine", "Cost: {0}.");

    /// <summary>Shown for a cost that is not known: before prices were recorded, or a team's own provider.</summary>
    public static readonly TextKey NoCost = new("team.support.usage.noCost", "-");

    /// <summary>The row adding up every row.</summary>
    public static readonly TextKey Total = new("team.support.usage.total", "Total");

    /// <summary>A provider or model that was not named.</summary>
    public static readonly TextKey Unknown = new("team.support.usage.unknown", "Unknown");

    /// <summary>When the assistant was not used in the period.</summary>
    public static readonly TextKey NoUsage = new("team.support.usage.noUsage", "The assistant was not used in this period.");

    /// <summary>When the reader may not read the usage.</summary>
    public static readonly TextKey NotPermitted = new("team.support.usage.notPermitted", "You cannot see the assistant's usage.");

    /// <summary>Every key the component renders, for building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        From, To, Team, Provider, Model, Answers, Declines, ToolCalls, InputTokens, CachedTokens, OutputTokens, TotalTokens, Total, Unknown,
        NoUsage, NotPermitted, Cost, TotalsLine, CostLine, NoCost
    ];
}
