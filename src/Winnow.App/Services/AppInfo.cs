using System.Reflection;

namespace Winnow.App.Services;

/// <summary>Facts about this build of the application, for the Settings and About windows.</summary>
public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/gcorneau-home/winnowrss";

    /// <summary>The version from Directory.Build.props (or the release tag), without the commit hash .NET appends.</summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
}
