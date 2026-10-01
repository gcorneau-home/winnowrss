using System.Diagnostics;

namespace Winnow.App.Services;

/// <summary>Hands things over to Windows (default browser, etc.).</summary>
public interface IShellService
{
    void OpenInBrowser(string url);
}

public sealed class ShellService : IShellService
{
    public void OpenInBrowser(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
