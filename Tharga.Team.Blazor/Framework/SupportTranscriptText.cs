namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportTranscript</c> — the transcript both back-office queues share.
/// </summary>
/// <remarks>
/// Its own catalogue rather than a key borrowed from either queue, because the component is rendered by both
/// and a key owned by one of them would be a dependency in the wrong direction.
/// </remarks>
public static class SupportTranscriptText
{
    /// <summary>
    /// Marks an entry an assistant wrote rather than a person.
    /// </summary>
    /// <remarks>
    /// Shown to support staff, not to the customer: an agent picking up a case needs to know what the
    /// customer has already been told by an assistant, because it carries different weight from what a
    /// colleague told them.
    /// </remarks>
    public static readonly TextKey Assistant = new("team.support.transcript.assistant", "Assistant");

    /// <summary>Names the icon on an answer from a person on the support side rather than the one who raised the case.</summary>
    public static readonly TextKey Support = new("team.support.transcript.support", "Support");

    /// <summary>Under an answer the case's channels have not taken yet.</summary>
    public static readonly TextKey NotSentYet = new("team.support.transcript.notSentYet", "Not sent yet");

    /// <summary>Under an answer a channel refused or that bounced; the reason follows it.</summary>
    public static readonly TextKey NotDelivered = new("team.support.transcript.notDelivered", "Not delivered");

    /// <summary>The button that sends an undelivered answer again.</summary>
    public static readonly TextKey SendAgain = new("team.support.transcript.sendAgain", "Send again");

    /// <summary>Read out for the dots shown while somebody on the other side is writing; <c>{0}</c> is their name.</summary>
    public static readonly TextKey Typing = new("team.support.transcript.typing", "{0} is typing…");

    /// <summary>Before the assistant's dots once it has gone round the tool loop: <c>{0}</c> is how many times it has. The dots stand for the "…".</summary>
    public static readonly TextKey WorkingLoop = new("team.support.transcript.workingLoop", "Working {0}");

    /// <summary>
    /// <see cref="WorkingLoop"/> with how long the assistant has been at it: <c>{0}</c> is how many times it has gone round, <c>{1}</c> the time as the two
    /// texts below make it, such as "1m 23s". One text, so a translation puts both where its language wants them.
    /// </summary>
    public static readonly TextKey WorkingLoopTimed = new("team.support.transcript.workingLoopTimed", "Working {0} ({1})");

    /// <summary><see cref="ThinkingLoop"/> with the time: <c>{0}</c> is how many times it has gone round, <c>{1}</c> the time.</summary>
    public static readonly TextKey ThinkingLoopTimed = new("team.support.transcript.thinkingLoopTimed", "Thinking {0} ({1})");

    /// <summary>Any other label of the assistant's dots with the time after it: <c>{0}</c> is the label, <c>{1}</c> the time.</summary>
    public static readonly TextKey Timed = new("team.support.transcript.timed", "{0} ({1})");

    /// <summary>A time under a minute: <c>{0}</c> is the seconds.</summary>
    public static readonly TextKey ElapsedSeconds = new("team.support.transcript.elapsedSeconds", "{0}s");

    /// <summary>A time of a minute or more: <c>{0}</c> is the minutes, <c>{1}</c> the seconds left over.</summary>
    public static readonly TextKey ElapsedMinutes = new("team.support.transcript.elapsedMinutes", "{0}m {1}s");

    /// <summary>Beside the assistant's dots while a reasoning model thinks, before it writes or asks for tools. The dots stand for the "…".</summary>
    public static readonly TextKey Thinking = new("team.support.transcript.thinking", "Thinking");

    /// <summary>The same once the assistant has gone round the tool loop: <c>{0}</c> is how many times it has.</summary>
    public static readonly TextKey ThinkingLoop = new("team.support.transcript.thinkingLoop", "Thinking {0}");

    /// <summary>Opens the list under an answer of the one service call the assistant made for it.</summary>
    public static readonly TextKey ServiceCall = new("team.support.transcript.serviceCall", "1 service call");

    /// <summary>Opens the list under an answer of the service calls the assistant made for it: <c>{0}</c> is how many.</summary>
    public static readonly TextKey ServiceCalls = new("team.support.transcript.serviceCalls", "{0} service calls");

    /// <summary>Beside the service calls under an answer: <c>{0}</c> is how many tokens the model reported for it.</summary>
    public static readonly TextKey Tokens = new("team.support.transcript.tokens", "{0} tokens");

    /// <summary>In the details under an answer, the tokens split: <c>{0}</c> read, <c>{1}</c> written.</summary>
    public static readonly TextKey TokensInOut = new("team.support.transcript.tokensInOut", "Tokens: {0} in, {1} out");

    /// <summary>The button that rates one of the assistant's answers as helpful, or takes that back.</summary>
    public static readonly TextKey Helpful = new("team.support.transcript.helpful", "Helpful");

    /// <summary>The button that rates one of the assistant's answers as not helpful, or takes that back.</summary>
    public static readonly TextKey NotHelpful = new("team.support.transcript.notHelpful", "Not helpful");

    /// <summary>Marks an answer the person it answered found helpful, where it cannot be rated, as for support.</summary>
    public static readonly TextKey RatedHelpful = new("team.support.transcript.ratedHelpful", "Rated helpful");

    /// <summary>Marks an answer the person it answered found unhelpful, where it cannot be rated.</summary>
    public static readonly TextKey RatedNotHelpful = new("team.support.transcript.ratedNotHelpful", "Rated not helpful");

    /// <summary>To the member, once their message to a person has waited a while with no answer.</summary>
    public static readonly TextKey NoAnswerYet = new("team.support.transcript.noAnswerYet",
        "Nobody from support has answered yet. We will get back to you here as soon as we can.");

    /// <summary>The same, at once, while support is away.</summary>
    public static readonly TextKey NoAnswerAway = new("team.support.transcript.noAnswerAway",
        "Support is away right now. We will get back to you here as soon as we can.");

    /// <summary>Before the reason support gave for closing a case, under the closure.</summary>
    public static readonly TextKey ClosedBecause = new("team.support.transcript.closedBecause", "Reason:");

    /// <summary>The button on a received mail that saves it as it arrived, as an <c>.eml</c> file a mail client opens.</summary>
    public static readonly TextKey DownloadOriginal = new("team.support.transcript.downloadOriginal", "Download original (.eml)");

    /// <summary>The button on a received mail that shows it as it arrived, as plain text.</summary>
    public static readonly TextKey ReadOriginal = new("team.support.transcript.readOriginal", "Read original");

    /// <summary>The title of the window that shows a received mail as it arrived.</summary>
    public static readonly TextKey OriginalTitle = new("team.support.transcript.originalTitle", "Original mail");

    /// <summary>Under a received mail whose original could not be read, though it was kept.</summary>
    public static readonly TextKey OriginalNotAvailable = new("team.support.transcript.originalNotAvailable", "The original mail could not be opened.");

    /// <summary>Above the text of an original too long to show whole; the download has all of it.</summary>
    public static readonly TextKey OriginalCut = new("team.support.transcript.originalCut", "Only the start is shown. Download the original for all of it.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        ClosedBecause, Assistant, Support, NotSentYet, NotDelivered, SendAgain, Typing, WorkingLoop, WorkingLoopTimed, Thinking, ThinkingLoop, ThinkingLoopTimed, Timed, ElapsedSeconds, ElapsedMinutes, ServiceCall, ServiceCalls, Tokens, TokensInOut,
        Helpful, NotHelpful, RatedHelpful, RatedNotHelpful, NoAnswerYet, NoAnswerAway, DownloadOriginal, ReadOriginal, OriginalTitle, OriginalNotAvailable, OriginalCut
    ];
}
