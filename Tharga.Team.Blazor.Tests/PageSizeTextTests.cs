using System.Text.RegularExpressions;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A grid that offers a page-size picker labels it from its own catalogue, so a host can translate the label rather than
/// show the grid library's English default beside translated text.
/// </summary>
public class PageSizeTextTests
{
    public sealed record Grid(string Path, string Catalogue, TextKey Key, TextKey[] All)
    {
        public override string ToString() => Catalogue;
    }

    private static readonly Grid[] Offering =
    [
        new("Features/Audit/AuditLogView.razor", nameof(AuditLogViewText), AuditLogViewText.PageSize, AuditLogViewText.All),
        new("Features/Team/TeamComponent.razor", nameof(TeamComponentText), TeamComponentText.PageSize, TeamComponentText.All),
    ];

    public static TheoryData<Grid> Grids() => new(Offering);

    // A quoted attribute value may hold a ">" (a generic TItem), so the tag is read value by value.
    private const string OpeningTag = @"<RadzenDataGrid\b(?:[^>""]|""[^""]*"")*?PageSizeOptions=(?:[^>""]|""[^""]*"")*>";

    private static string ComponentSource(string relativePath)
        => File.ReadAllText(Path.Combine(FindProject(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    [Theory]
    [MemberData(nameof(Grids))]
    public void TheLabel_IsResolvedWithTheViewsOtherText(Grid grid)
    {
        Assert.Contains(grid.Key, grid.All);
        Assert.Equal("items per page", grid.Key.Default);
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void TheGridWithAPageSizePicker_LabelsItFromItsOwnCatalogue(Grid view)
    {
        var grid = Regex.Match(ComponentSource(view.Path), OpeningTag, RegexOptions.Singleline);

        Assert.True(grid.Success, $"{view.Path} has no grid with a page-size picker.");
        Assert.Contains($"PageSizeText=\"@_text[{view.Catalogue}.PageSize]\"", grid.Value);
    }

    [Fact]
    public void EveryGridWithAPageSizePicker_IsCovered()
    {
        var offering = Directory.GetFiles(FindProject(), "*.razor", SearchOption.AllDirectories)
            .Where(x => Regex.IsMatch(File.ReadAllText(x), OpeningTag, RegexOptions.Singleline))
            .Select(x => Path.GetRelativePath(FindProject(), x).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(x => x)
            .ToArray();

        Assert.Equal(Offering.Select(x => x.Path).OrderBy(x => x).ToArray(), offering);
    }

    private static string FindProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "Tharga.Team.Blazor");
    }
}
