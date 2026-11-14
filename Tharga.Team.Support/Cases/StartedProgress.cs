namespace Tharga.Team.Support.Cases;

/// <summary>Passes the assistant's progress on with when it started on the question, so a display counts up from the real start.</summary>
internal sealed class StartedProgress(IProgress<SupportAssistantProgress> inner, DateTime startedAt) : IProgress<SupportAssistantProgress>
{
    public void Report(SupportAssistantProgress value) => inner.Report(value with { StartedAt = startedAt });
}
