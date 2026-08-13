namespace DovahLink.BridgeBuilder.Build;

public sealed record VisualStudioToolchain(string VcvarsallPath, string VcpkgRoot);

public static class BuildCommand
{
    /// <summary>
    /// Creates the Windows command used to configure and build the bridge plugin.
    /// </summary>
    /// <param name="toolchain">The Visual Studio and vcpkg paths used by the build.</param>
    /// <returns>A command that initializes the x64 environment, sets the vcpkg root, configures CMake, and builds the bridge plugin.</returns>
    public static string Create(VisualStudioToolchain toolchain)
    {
        return $"call \"{toolchain.VcvarsallPath}\" x64 && " +
               $"set \"VCPKG_ROOT={toolchain.VcpkgRoot}\" && " +
               "cmake --preset windows-x64-release && " +
               "cmake --build --preset windows-x64-release --target dovahlink_bridge_plugin";
    }
}
