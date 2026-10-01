using System.IO.Compression;
using System.Net;
using System.Text;
using Winnow.Core.Services;
using Winnow.Core.Theming;

namespace Winnow.Core.Tests;

public class ThemingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"winnow-themes-{Guid.NewGuid():N}");

    // Dracula-like theme, with what real themes contain: comments, trailing commas, a URL in a string, alpha colors.
    private const string Dark = """
        {
            "$schema": "vscode://schemas/color-theme", // not a comment start inside a string
            "name": "Vampire",
            "colors": {
                "editor.background": "#282A36",
                "editor.foreground": "#F8F8F2",
                "sideBar.background": "#21222C",
                "focusBorder": "#6272A4",
                "list.hoverBackground": "#44475A75", /* 46% opaque, alpha last */
                "errorForeground": "#FF5555",
            },
        }
        """;

    [Theory]
    [InlineData("#282A36", 255, 0x28, 0x2A, 0x36)]
    [InlineData("#44475A75", 0x75, 0x44, 0x47, 0x5A)]
    [InlineData("#fff", 255, 255, 255, 255)]
    public void Colors_are_read_in_VS_Code_order(string text, int a, int r, int g, int b)
    {
        Assert.Equal(new ThemeColor((byte)a, (byte)r, (byte)g, (byte)b), ThemeColor.TryParse(text));
        Assert.Null(ThemeColor.TryParse("red"));
    }

    [Fact]
    public void A_theme_file_maps_to_a_palette_with_fallbacks()
    {
        var entry = VsCodeTheme.Parse(Dark, "fallback");
        var palette = ThemePaletteMapper.FromVsCode(entry, "vampire", "test");

        Assert.Equal("Vampire", palette.Name);
        Assert.True(palette.IsDark); // no "type": guessed from the dark background
        Assert.Equal("#282A36", palette.Background.ToString());
        Assert.Equal("#21222C", palette.Surface.ToString());
        Assert.Equal("#6272A4", palette.Accent.ToString());
        Assert.Equal("#6272A4", palette.Link.ToString()); // no textLink.foreground: the accent
        Assert.Equal(255, palette.Hover.A);               // translucent colors are flattened
        Assert.Equal("#FF5555", palette.Error.ToString());
        Assert.Contains("background: #282A36", palette.ArticleCss());
    }

    [Fact]
    public void A_vsix_yields_every_theme_it_declares_including_extended_ones()
    {
        var vsix = Vsix(new()
        {
            ["extension/package.json"] = """
                { "contributes": { "themes": [
                    { "label": "Vampire", "uiTheme": "vs-dark", "path": "./themes/vampire.json" },
                    { "label": "Vampire Day", "uiTheme": "vs", "path": "./themes/day.json" } ] } }
                """,
            ["extension/themes/vampire.json"] = Dark,
            ["extension/themes/day.json"] = """{ "include": "./vampire.json", "colors": { "editor.background": "#FAFAFA" } }""",
        });

        var themes = VsCodeTheme.ReadVsix(vsix);

        Assert.Equal(["Vampire", "Vampire Day"], themes.Select(t => t.Name));
        var day = themes[1];
        Assert.False(day.IsDark);                       // from package.json "uiTheme": "vs"
        Assert.Equal("#FAFAFA", day.Colors["editor.background"]);
        Assert.Equal("#6272A4", day.Colors["focusBorder"]); // inherited through "include"
    }

    [Fact]
    public void The_library_imports_lists_replaces_and_deletes_themes()
    {
        var library = new ThemeLibrary(_folder);
        var file = Path.Combine(Path.GetTempPath(), $"vampire-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, Dark);
        try
        {
            var imported = Assert.Single(library.ImportFile(file));
            library.ImportFile(file); // same theme again: replaced, not duplicated

            var listed = Assert.Single(library.GetAll());
            Assert.Equal(imported, listed);
            Assert.Equal(imported, library.Get(imported.Id));

            library.Delete(imported.Id);
            Assert.Empty(library.GetAll());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Importing_something_that_is_not_a_theme_is_a_readable_error()
    {
        var library = new ThemeLibrary(_folder);
        var file = Path.Combine(Path.GetTempPath(), $"broken-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, "[1, 2, 3]");
        try
        {
            Assert.Equal(WinnowError.ThemeInvalid, Assert.Throws<WinnowException>(() => library.ImportFile(file)).Error);
            var empty = Vsix(new() { ["extension/package.json"] = """{ "contributes": {} }""" });
            Assert.Equal(WinnowError.ThemeNone, Assert.Throws<WinnowException>(() => library.ImportVsix(empty, "x")).Error);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Open_VSX_search_and_download()
    {
        var handler = new FakeOpenVsx();
        var client = new OpenVsxClient(new HttpClient(handler), "https://registry.test/");

        var result = Assert.Single(await client.SearchAsync("dracula theme"));
        var package = await client.DownloadAsync(result);

        Assert.Equal("https://registry.test/api/-/search?category=Themes&size=30&query=dracula%20theme", handler.Requests[0]);
        Assert.Equal(("dracula-theme.theme-dracula", "Dracula Official", 1234L), (result.Id, result.DisplayName, result.Downloads));
        Assert.Equal([1, 2, 3], package);
    }

    [Fact]
    public async Task An_unreachable_catalog_is_a_readable_error()
    {
        var client = new OpenVsxClient(new HttpClient(new FakeOpenVsx { Fail = true }), "https://registry.test/");

        var error = await Assert.ThrowsAsync<WinnowException>(() => client.SearchAsync("nord"));
        Assert.Equal(WinnowError.ThemeCatalogUnavailable, error.Error);
    }

    /// <summary>Checks a real theme package: set WINNOWRSS_VSIX to a .vsix file (e.g. downloaded from open-vsx.org).</summary>
    [VsixFact]
    public void A_real_vsix_imports()
    {
        var palettes = new ThemeLibrary(_folder).ImportFile(Environment.GetEnvironmentVariable("WINNOWRSS_VSIX")!);

        Assert.NotEmpty(palettes);
        Assert.All(palettes, p => Assert.NotEqual(p.Background, p.Foreground));
    }

    public sealed class VsixFactAttribute : FactAttribute
    {
        public VsixFactAttribute()
        {
            if (!File.Exists(Environment.GetEnvironmentVariable("WINNOWRSS_VSIX")))
                Skip = "Set WINNOWRSS_VSIX to a .vsix file to check a real theme package.";
        }
    }

    private static MemoryStream Vsix(Dictionary<string, string> files)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, content) in files)
                using (var writer = new StreamWriter(zip.CreateEntry(path).Open(), Encoding.UTF8))
                    writer.Write(content);
        stream.Position = 0;
        return stream;
    }

    private sealed class FakeOpenVsx : HttpMessageHandler
    {
        public bool Fail { get; init; }
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            if (Fail)
                throw new HttpRequestException("No such host");
            var content = request.RequestUri.AbsolutePath.EndsWith(".vsix")
                ? new ByteArrayContent([1, 2, 3])
                : new StringContent("""
                    { "extensions": [ { "namespace": "dracula-theme", "name": "theme-dracula", "displayName": "Dracula Official",
                      "version": "2.25.1", "downloadCount": 1234,
                      "files": { "download": "https://registry.test/api/dracula-theme/theme-dracula/2.25.1/file/d.vsix" } } ] }
                    """, Encoding.UTF8, "application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }
}
