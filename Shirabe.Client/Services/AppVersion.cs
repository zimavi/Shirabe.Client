using System.Reflection;

namespace Shirabe.Client.Services;

/// <summary>
/// The version the build was stamped with. It comes from <c>version.txt</c> at the repository root plus the commit
/// (see Directory.Build.props), stored in the assembly as <c>0.1.0+ce863a5e7</c>.
/// </summary>
public sealed record AppVersion(string Version, string? Commit)
{
    private const int CommitLength = 9;

    public static AppVersion Current { get; } =
        Parse(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion);

    /// <summary>Splits <c>version+commit</c>; no version means "unknown", no commit means a build outside a git checkout.</summary>
    public static AppVersion Parse(string? informationalVersion)
    {
        var plus = informationalVersion?.IndexOf('+') ?? -1;
        var version = plus < 0 ? informationalVersion : informationalVersion![..plus];
        var commit = plus < 0 ? "" : informationalVersion![(plus + 1)..];

        return new AppVersion(
            string.IsNullOrWhiteSpace(version) ? "unknown" : version,
            commit.Length == 0 ? null : commit[..Math.Min(commit.Length, CommitLength)]);
    }
}