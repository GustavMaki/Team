using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// How long the support assistant has been working, beside its dots: "Working 2 (1m 23s)", counting up each second from when it started on the question,
/// worded by translatable texts, and kept under the finished answer.
/// </summary>
public class SupportAssistantElapsedTests : BunitContext
{
    private static readonly DateTime Started = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private static TextSet Texts() => new DefaultThargaTextProvider().ResolveAsync(SupportTranscriptText.All).GetAwaiter().GetResult();

    private static SupportAssistantProgress Round(int round, bool tools = false, bool thinking = false, DateTime? startedAt = null)
        => new() { Round = round, Tools = tools ? ["search_my_cases"] : [], Thinking = thinking, StartedAt = startedAt ?? Started };

    // ---- the time

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(23, "23s")]
    [InlineData(59, "59s")]
    [InlineData(60, "1m 0s")]
    [InlineData(83, "1m 23s")]
    [InlineData(3725, "62m 5s")]
    public void TheTime_IsSecondsUnderAMinute_AndMinutesAndSecondsFromThere(int seconds, string expected)
        => Assert.Equal(expected, SupportAssistantSteps.Elapsed(Texts(), TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void AClockBehindTheStart_ReadsAsTheStart_NeverNegative()
        => Assert.Equal(TimeSpan.Zero, SupportAssistantSteps.SinceStart(Round(1), Started.AddSeconds(-5)));

    [Fact]
    public void WithNoStartKnown_ThereIsNoTime()
        => Assert.Null(SupportAssistantSteps.SinceStart(new SupportAssistantProgress { Round = 2 }, Started));

    // ---- the label

    [Fact]
    public void WorkingTwo_AfterAMinuteAndTwentyThreeSeconds_ReadsTheWayTheIssueAsks()
        => Assert.Equal("Working 2 (1m 23s)", SupportAssistantSteps.Describe(Texts(), Round(3), "Working", Started.AddSeconds(83)));

    [Fact]
    public void UnderAMinute_ItIsSeconds()
        => Assert.Equal("Working 2 (23s)", SupportAssistantSteps.Describe(Texts(), Round(3), "Working", Started.AddSeconds(23)));

    [Fact]
    public void BeforeTheToolLoop_TheHostsOwnLabelGetsTheTime()
        => Assert.Equal("The assistant is thinking (12s)", SupportAssistantSteps.Describe(Texts(), Round(1), "The assistant is thinking", Started.AddSeconds(12)));

    [Fact]
    public void WhileAReasoningModelThinks_ThatLabelGetsTheTimeToo()
    {
        Assert.Equal("Thinking (5s)", SupportAssistantSteps.Describe(Texts(), Round(1, thinking: true), "Working", Started.AddSeconds(5)));
        Assert.Equal("Thinking 1 (1m 5s)", SupportAssistantSteps.Describe(Texts(), Round(2, thinking: true), "Working", Started.AddSeconds(65)));
    }

    [Fact]
    public void UnderTextAlreadyWritten_AServiceCallAlsoShowsTheTime()
        => Assert.Equal("Working 1 (9s)", SupportAssistantSteps.Running(Texts(), Round(1, tools: true), Started.AddSeconds(9)));

    [Fact]
    public void WithNoClock_OrNoStart_TheLabelsAreWhatTheyAlwaysWere()
    {
        Assert.Equal("Working 2", SupportAssistantSteps.Describe(Texts(), Round(3), "Working"));
        Assert.Equal("Working 2", SupportAssistantSteps.Describe(Texts(), new SupportAssistantProgress { Round = 3 }, "Working", Started.AddSeconds(30)));
        Assert.Equal("Working", SupportAssistantSteps.Describe(Texts(), new SupportAssistantProgress(), "Working", Started));
        Assert.Equal("Working 1", SupportAssistantSteps.Running(Texts(), new SupportAssistantProgress { Round = 1, Tools = ["x"] }));
    }

    [Fact]
    public async Task TheWholeTextIsOneTranslation_WithThePassAndTheTimeWhereTheLanguageWantsThem()
    {
        var swedish = await new Translating(new Dictionary<string, string>
        {
            [SupportTranscriptText.WorkingLoopTimed.Key] = "Arbetar {0} ({1})",
            [SupportTranscriptText.ElapsedMinutes.Key] = "{0} min {1} s",
            [SupportTranscriptText.ElapsedSeconds.Key] = "{0} s"
        }).ResolveAsync(SupportTranscriptText.All);

        Assert.Equal("Arbetar 2 (1 min 23 s)", SupportAssistantSteps.Describe(swedish, Round(3), "Arbetar", Started.AddSeconds(83)));
        Assert.Equal("Arbetar 2 (23 s)", SupportAssistantSteps.Describe(swedish, Round(3), "Arbetar", Started.AddSeconds(23)));
    }

    [Fact]
    public async Task AHostCanPutTheTimeBeforeThePass()
    {
        var reordered = await new Translating(new Dictionary<string, string> { [SupportTranscriptText.WorkingLoopTimed.Key] = "({1}) round {0}" }).ResolveAsync(SupportTranscriptText.All);

        Assert.Equal("(23s) round 2", SupportAssistantSteps.Describe(reordered, Round(3), "Working", Started.AddSeconds(23)));
    }

    // ---- under the finished answer

    [Fact]
    public void TheFinishedAnswer_SaysHowLongItTook_BesideItsCallsAndTokens()
    {
        var calls = new[] { new SupportAssistantCall { Name = "search_my_cases", Arguments = "text: export" } };
        var usage = new SupportAssistantUsage { TotalTokens = 830, Elapsed = TimeSpan.FromSeconds(83) };

        Assert.Equal("1 service call · 830 tokens · 1m 23s", SupportAssistantSteps.Summary(Texts(), calls, usage));
    }

    [Fact]
    public void AnAnswerWithNoTokensReported_StillSaysHowLongItTook()
        => Assert.Equal("12s", SupportAssistantSteps.Summary(Texts(), [], new SupportAssistantUsage { Elapsed = TimeSpan.FromSeconds(12) }));

    [Fact]
    public void AnAnswerKeptBeforeTheTimeWasRecorded_SaysWhatItAlwaysDid()
        => Assert.Equal("830 tokens", SupportAssistantSteps.Summary(Texts(), [], new SupportAssistantUsage { TotalTokens = 830 }));

    // ---- counting up

    private (IRenderedComponent<SupportAssistantStatus> View, ManualTime Time) Render(SupportAssistantProgress progress, bool step = false)
    {
        var time = new ManualTime(Started);
        Services.AddSingleton<TimeProvider>(time);

        return (Render<SupportAssistantStatus>(p => p.Add(x => x.Text, Texts()).Add(x => x.Progress, progress).Add(x => x.Working, "Working").Add(x => x.Step, step)), time);
    }

    [Fact]
    public void TheTimeCountsUp_EachSecond_WithoutAnythingElseHappening()
    {
        var (view, time) = Render(Round(3));
        Assert.Equal("Working 2 (0s)", view.Find(".support-typing__label").TextContent);

        time.Advance(TimeSpan.FromSeconds(1));
        view.WaitForAssertion(() => Assert.Equal("Working 2 (1s)", view.Find(".support-typing__label").TextContent));

        time.Advance(TimeSpan.FromSeconds(82));
        view.WaitForAssertion(() => Assert.Equal("Working 2 (1m 23s)", view.Find(".support-typing__label").TextContent));
    }

    [Fact]
    public void ItCountsFromWhenTheAssistantStarted_NotFromWhenThePageWasOpened()
    {
        var time = new ManualTime(Started.AddSeconds(95));
        Services.AddSingleton<TimeProvider>(time);

        var view = Render<SupportAssistantStatus>(p => p.Add(x => x.Text, Texts()).Add(x => x.Progress, Round(3)).Add(x => x.Working, "Working"));

        Assert.Equal("Working 2 (1m 35s)", view.Find(".support-typing__label").TextContent);
    }

    [Fact]
    public void UnderTextAlreadyWritten_TheStepLineCountsToo()
    {
        var (view, time) = Render(Round(2, tools: true), step: true);

        time.Advance(TimeSpan.FromSeconds(7));

        view.WaitForAssertion(() => Assert.Equal("Working 2 (7s)", view.Find(".support-typing__label").TextContent));
    }

    [Fact]
    public void WithNoStart_NothingTicks_AndTheLabelIsAsItAlwaysWas()
    {
        var (view, time) = Render(new SupportAssistantProgress { Round = 3 });

        Assert.Equal("Working 2", view.Find(".support-typing__label").TextContent);
        Assert.Equal(0, time.Timers);
    }

    [Fact]
    public void TheTimerStopsWithTheComponent()
    {
        var (view, time) = Render(Round(3));
        Assert.Equal(1, time.Timers);

        view.Instance.Dispose();

        Assert.Equal(0, time.Timers);
    }

    private sealed class Translating(Dictionary<string, string> texts) : IThargaTextProvider
    {
        public Task<string> GetAsync(TextKey key) => Task.FromResult(texts.GetValueOrDefault(key.Key) ?? key.Default);
    }

    /// <summary>A clock that moves when a test says, and runs the timers it was asked for when it passes their time.</summary>
    private sealed class ManualTime(DateTime start) : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(start, TimeSpan.Zero);

        public int Timers => _timers.Count;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);

            return timer;
        }

        public void Advance(TimeSpan by)
        {
            var end = _now + by;
            while (_now < end)
            {
                _now += TimeSpan.FromSeconds(1) <= end - _now ? TimeSpan.FromSeconds(1) : end - _now;
                foreach (var timer in _timers.ToArray()) timer.Tick();
            }
        }

        internal void Remove(ManualTimer timer) => _timers.Remove(timer);

        internal sealed class ManualTimer(ManualTime owner, TimerCallback callback, object state) : ITimer
        {
            public void Tick() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period1) => true;

            public void Dispose() => owner.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();

                return ValueTask.CompletedTask;
            }
        }
    }
}
