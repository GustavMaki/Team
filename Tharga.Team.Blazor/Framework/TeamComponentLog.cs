using Microsoft.Extensions.Logging;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// What the team selector and the suspended-team notice log when an event handler fails. Kept out of the components: a log line is not display text,
/// and the text-coverage scan counts every string literal in a component's markup and code.
/// </summary>
/// <remarks>
/// These run in handlers of events that return <c>void</c>, so nothing awaits them and an exception that escaped would end the circuit.
/// </remarks>
internal static partial class TeamComponentLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The team selector could not show the changed selection.")]
    public static partial void SelectionNotShown(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The team selector could not show that the team was refused.")]
    public static partial void RefusalNotShown(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The team selector could not reload the team list.")]
    public static partial void TeamListNotReloaded(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The suspended-team notice could not be refreshed after the selection changed.")]
    public static partial void SuspendedNoticeNotRefreshed(ILogger logger, Exception exception);
}
