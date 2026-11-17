using Microsoft.JSInterop;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Copying to the clipboard from a click handler, without letting a refusal end the circuit.
/// </summary>
internal static class ClipboardExtensions
{
    /// <summary>Puts <paramref name="text"/> on the clipboard; returns whether the browser took it.</summary>
    /// <remarks>
    /// <b>The browser may refuse</b>: outside a secure page there is no clipboard, and a user can deny the permission. That surfaces as an exception from the
    /// interop call, and an exception that leaves a Blazor Server event handler ends the circuit, so the copy button would take the page down with it. Nothing is
    /// claimed as copied then, which is what the caller does with the <c>false</c>.
    /// </remarks>
    /// <param name="js">The JavaScript runtime of the component.</param>
    /// <param name="text">What to copy.</param>
    /// <param name="function">The browser function that writes it; the toolkit's own by default.</param>
    public static async Task<bool> TryCopyToClipboardAsync(this IJSRuntime js, string text, string function = "clipboard.copyText")
    {
        try
        {
            await js.InvokeVoidAsync(function, text);

            return true;
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException)
        {
            return false;
        }
    }
}
