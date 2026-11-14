using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>What the assistant is doing, beside its dots, and the service calls it made, under its answer.</summary>
internal static class SupportAssistantSteps
{
    /// <summary>
    /// <paramref name="working"/> until the model has gone round the tool loop, then "Working 2…" with how many times it
    /// has. <paramref name="text"/> holds <see cref="SupportTranscriptText"/>. With <paramref name="now"/> and a start on the
    /// progress, the time the assistant has been at it follows: "Working 2 (1m 23s)".
    /// </summary>
    public static string Describe(TextSet text, SupportAssistantProgress progress, string working, DateTime? now = null)
    {
        var loops = LoopsOf(progress);
        var time = SinceStart(progress, now);

        if (progress is { Thinking: true })
        {
            if (loops > 0) return time == null ? text.Format(SupportTranscriptText.ThinkingLoop, loops) : text.Format(SupportTranscriptText.ThinkingLoopTimed, loops, Elapsed(text, time.Value));

            return WithTime(text, text[SupportTranscriptText.Thinking], time);
        }

        if (loops > 0) return time == null ? text.Format(SupportTranscriptText.WorkingLoop, loops) : text.Format(SupportTranscriptText.WorkingLoopTimed, loops, Elapsed(text, time.Value));

        return WithTime(text, working, time);
    }

    /// <summary>Under text already written: "Working 2…" while a service call runs, or null while none does.</summary>
    public static string Running(TextSet text, SupportAssistantProgress progress, DateTime? now = null)
    {
        if (progress?.Tools is not { Length: > 0 }) return null;

        var time = SinceStart(progress, now);

        return time == null ? text.Format(SupportTranscriptText.WorkingLoop, LoopsOf(progress)) : text.Format(SupportTranscriptText.WorkingLoopTimed, LoopsOf(progress), Elapsed(text, time.Value));
    }

    /// <summary>How long the assistant has been at it, or null when it is not known when it started. Never negative: a clock a moment behind the server's reads as the start.</summary>
    public static TimeSpan? SinceStart(SupportAssistantProgress progress, DateTime? now)
    {
        if (progress?.StartedAt is not { } started || now is not { } current) return null;

        var since = current - started;

        return since < TimeSpan.Zero ? TimeSpan.Zero : since;
    }

    /// <summary>"23s" under a minute and "1m 23s" from it, the units from <paramref name="text"/> so a translation words them.</summary>
    public static string Elapsed(TextSet text, TimeSpan time)
    {
        var seconds = (long)Math.Floor(time.TotalSeconds);

        return seconds < 60
            ? text.Format(SupportTranscriptText.ElapsedSeconds, seconds)
            : text.Format(SupportTranscriptText.ElapsedMinutes, seconds / 60, seconds % 60);
    }

    private static string WithTime(TextSet text, string label, TimeSpan? time)
        => time == null ? label : text.Format(SupportTranscriptText.Timed, label, Elapsed(text, time.Value));

    /// <summary>How many times the model has asked for service calls: the round it is on while they run, the one before once they are back.</summary>
    public static int LoopsOf(SupportAssistantProgress progress)
    {
        if (progress == null) return 0;

        return progress.Tools is { Length: > 0 } ? progress.Round : progress.Round - 1;
    }

    /// <summary>"2 service calls", for the list under an answer.</summary>
    public static string Counted(TextSet text, SupportAssistantCall[] calls)
        => calls.Length == 1 ? text[SupportTranscriptText.ServiceCall] : text.Format(SupportTranscriptText.ServiceCalls, calls.Length);

    /// <summary>"2 service calls · 830 tokens · 12s", what the folded details under an answer say; each part only when there is one.</summary>
    public static string Summary(TextSet text, SupportAssistantCall[] calls, SupportAssistantUsage usage)
    {
        List<string> parts = [];
        if (calls.Length > 0) parts.Add(Counted(text, calls));

        // An answer the provider reported no tokens for still says how long it took, and then has no token count to show.
        if (usage is { TotalTokens: > 0 }) parts.Add(text.Format(SupportTranscriptText.Tokens, usage.TotalTokens.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)));
        if (usage?.Elapsed is { } elapsed) parts.Add(Elapsed(text, elapsed));

        return string.Join(" · ", parts);
    }

    /// <summary>One call as a row: <c>search_my_cases(text: "export")</c>.</summary>
    public static string Row(SupportAssistantCall call) => $"{call.Name}({call.Arguments})";
}
