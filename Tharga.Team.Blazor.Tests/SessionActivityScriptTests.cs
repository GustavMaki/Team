namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The browser half of idle time (Tharga/Team#471): a small module that, like the support desk's presence reporter, watches for use of a visible tab and tells the
/// server at most once per interval, and the sign-in menu that loads it only when sessions are active. These read the files, as the panel style tests do; what the
/// server does with the report is <c>SessionActivityEndpointTests</c>.
/// </summary>
public class SessionActivityScriptTests
{
    private static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor"))) dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(dir.FullName, "Tharga.Team.Blazor", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{relativePath} does not exist.");
        return File.ReadAllText(path);
    }

    [Fact]
    public void TheModule_Exists_AndIsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(Read("wwwroot/session-activity.js")));
    }

    [Fact]
    public void TheModule_ExportsWhatItsPrecedentDoes_AWatcherAndAStop()
    {
        var script = Read("wwwroot/session-activity.js");

        Assert.Contains("export function watchActivity", script);
        Assert.Contains("export function stopWatching", script);
    }

    /// <summary>The set the support desk watches: pointer, key and wheel, so a person reading or typing counts and a tab left open does not.</summary>
    [Theory]
    [InlineData("pointerdown")]
    [InlineData("keydown")]
    [InlineData("wheel")]
    public void TheModule_WatchesForUse(string eventName)
    {
        Assert.Contains($"'{eventName}'", Read("wwwroot/session-activity.js"));
    }

    [Fact]
    public void TheModule_ReportsOnlyFromAVisibleTab_AndOnlyOncePerInterval()
    {
        var script = Read("wwwroot/session-activity.js");

        Assert.Contains("visibilityState", script);
        Assert.Contains("'visible'", script);
        Assert.Contains("intervalMs", script);
    }

    [Fact]
    public void TheModule_PostsToTheServersEndpoint_WithTheCookie_AndKeepsNothingInTheBrowser()
    {
        var script = Read("wwwroot/session-activity.js");

        Assert.Contains("/_tharga/session/activity", script);
        Assert.Contains("POST", script);
        Assert.Contains("same-origin", script);
        Assert.DoesNotContain("localStorage", script);
        Assert.DoesNotContain("sessionStorage", script);
    }

    [Fact]
    public void TheSignInMenu_LoadsTheModule_ForSessionsThatAreActive()
    {
        var razor = Read("Features/Authentication/LoginDisplay.razor");

        Assert.Contains("session-activity.js", razor);
        // Only when sessions are active: the reporter is a call a minute that does nothing for a host with no policy.
        Assert.Contains(".Active", razor);
    }
}
