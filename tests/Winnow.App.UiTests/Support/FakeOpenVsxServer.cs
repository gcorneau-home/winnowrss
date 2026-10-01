using System.IO.Compression;
using System.Net;
using System.Text;

namespace Winnow.App.UiTests.Support;

/// <summary>A tiny Open VSX: one search result, "Test Vampire", whose package holds one dark theme named "Vampire".</summary>
public sealed class FakeOpenVsxServer : IDisposable
{
    public const string ExtensionName = "Test Vampire";
    public const string ThemeName = "Vampire";

    private readonly HttpListener _listener = new();
    private readonly byte[] _package = Package();

    public FakeOpenVsxServer()
    {
        BaseUrl = $"http://localhost:{FeedServer.FreePort()}/";
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _ = ServeAsync();
    }

    public string BaseUrl { get; }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return;
            }

            if (context.Request.Url!.AbsolutePath.EndsWith(".vsix", StringComparison.Ordinal))
            {
                await context.Response.OutputStream.WriteAsync(_package);
            }
            else
            {
                context.Response.ContentType = "application/json";
                var search = $$"""
                    { "extensions": [ { "namespace": "test", "name": "vampire", "displayName": "{{ExtensionName}}",
                      "description": "A test theme", "version": "1.0.0", "downloadCount": 42,
                      "files": { "download": "{{BaseUrl}}files/test.vampire-1.0.0.vsix" } } ] }
                    """;
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(search));
            }
            context.Response.Close();
        }
    }

    private static byte[] Package()
    {
        var files = new Dictionary<string, string>
        {
            ["extension/package.json"] = $$"""{ "contributes": { "themes": [ { "label": "{{ThemeName}}", "uiTheme": "vs-dark", "path": "./vampire.json" } ] } }""",
            ["extension/vampire.json"] = """{ "colors": { "editor.background": "#282A36", "editor.foreground": "#F8F8F2", "focusBorder": "#BD93F9" } }""",
        };
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, content) in files)
                using (var writer = new StreamWriter(zip.CreateEntry(path).Open(), Encoding.UTF8))
                    writer.Write(content);
        return stream.ToArray();
    }

    public void Dispose() => _listener.Close();
}
