using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Winnow.App.UiTests.Support;

/// <summary>
/// Serves the feed fixtures on localhost (http://localhost:port/name.xml) so UI tests do not need the internet,
/// plus a PNG for any *.png path, counting image requests.
/// </summary>
public sealed class FeedServer : IDisposable
{
    // 1x1 transparent PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly HttpListener _listener = new();
    private int _imageRequests;

    public FeedServer()
    {
        var port = FreePort();
        BaseUrl = $"http://localhost:{port}/";
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _ = ServeAsync();
    }

    public string BaseUrl { get; }
    public string SampleFeedUrl => BaseUrl + "sample-feed.xml";
    public string AnchorsUrl => BaseUrl + "anchors.xml";
    public string ImagesUrl => BaseUrl + "images.xml";

    /// <summary>Requests received for images, whether served or not.</summary>
    public int ImageRequests => Volatile.Read(ref _imageRequests);

    /// <summary>Simulates the site going away: images answer 404.</summary>
    public bool ImagesOffline { get; set; }

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

            var response = context.Response;
            var name = Path.GetFileName(context.Request.Url!.AbsolutePath);
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _imageRequests);
                response.Headers["Cache-Control"] = "no-store"; // so the browser cache cannot hide a request
                if (ImagesOffline)
                {
                    response.StatusCode = 404;
                }
                else
                {
                    response.ContentType = "image/png";
                    await response.OutputStream.WriteAsync(Png);
                }
            }
            else if (File.Exists(fixture))
            {
                var xml = (await File.ReadAllTextAsync(fixture)).Replace("{BASE}", BaseUrl.TrimEnd('/'));
                response.ContentType = "application/rss+xml; charset=utf-8";
                await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(xml));
            }
            else
            {
                response.StatusCode = 404;
            }
            response.Close();
        }
    }

    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose() => _listener.Close();
}
