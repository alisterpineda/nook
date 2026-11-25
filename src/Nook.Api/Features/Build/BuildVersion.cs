using System.Reflection;

namespace Nook.Api.Features.Build;

/// <summary>
/// The version this build reports: the assembly's informational version, which holds the release number and the
/// first 7 characters of the commit it was built from, as in 0.1.0+abc1234.
/// </summary>
public static class BuildVersion
{
    public static string Current { get; } =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
