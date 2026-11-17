using Bunit;
using Microsoft.JSInterop;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A copy button must not end the circuit when the browser refuses the clipboard (Tharga/Team#406): outside a secure page there is none, and a user can deny
/// the permission. The refusal comes back as an exception from the interop call, and one that leaves a Blazor Server event handler ends the circuit.
/// </summary>
public class ClipboardExtensionsTests : BunitContext
{
    [Fact]
    public async Task WhenTheBrowserTakesIt_ItReportsTheCopy()
    {
        var call = JSInterop.SetupVoid("clipboard.copyText", "secret");
        call.SetVoidResult();

        Assert.True(await JSInterop.JSRuntime.TryCopyToClipboardAsync("secret"));

        call.VerifyInvoke("clipboard.copyText", 1);
    }

    [Fact]
    public async Task WhenTheBrowserRefuses_NothingIsClaimedAndNothingThrows()
    {
        JSInterop.SetupVoid("clipboard.copyText", "secret").SetException(new JSException("Write permission denied."));

        Assert.False(await JSInterop.JSRuntime.TryCopyToClipboardAsync("secret"));
    }

    [Fact]
    public async Task WhenTheCircuitIsGone_NothingIsClaimedAndNothingThrows()
    {
        JSInterop.SetupVoid("clipboard.copyText", "secret").SetException(new JSDisconnectedException("The circuit is gone."));

        Assert.False(await JSInterop.JSRuntime.TryCopyToClipboardAsync("secret"));
    }

    [Fact]
    public async Task ItCallsTheFunctionItIsGiven()
    {
        var call = JSInterop.SetupVoid("navigator.clipboard.writeText", "text");
        call.SetVoidResult();

        Assert.True(await JSInterop.JSRuntime.TryCopyToClipboardAsync("text", "navigator.clipboard.writeText"));

        call.VerifyInvoke("navigator.clipboard.writeText", 1);
    }
}
