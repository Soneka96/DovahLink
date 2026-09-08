using System.Collections.ObjectModel;

namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Converts Visual Studio's environment output into child-process environment values.</summary>
public static class VisualStudioEnvironment
{
    /// <summary>Parses environment lines and applies the validated bundled vcpkg root.</summary>
    /// <param name="environmentLines">The <c>set</c> output produced after importing the developer environment.</param>
    /// <param name="toolchain">The validated toolchain whose vcpkg root overrides inherited values.</param>
    /// <returns>A case-insensitive, read-only environment map for CMake.</returns>
    public static IReadOnlyDictionary<string, string> Create(
        IEnumerable<string> environmentLines,
        VisualStudioToolchain toolchain)
    {
        VisualStudioToolchain validated = VisualStudioToolchainLocator.Validate(toolchain);
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in environmentLines)
        {
            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            environment[line[..separator]] = line[(separator + 1)..];
        }
        environment["VCPKG_ROOT"] = validated.VcpkgRoot;
        return new ReadOnlyDictionary<string, string>(environment);
    }
}
