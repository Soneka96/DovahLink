namespace DovahLink.BridgeBuilder.Build;

public sealed record VisualStudioToolchain(string VcvarsallPath, string VcpkgRoot);

public static class BuildCommand
{
    public static string Create(VisualStudioToolchain toolchain)
    {
        return $"call \"{toolchain.VcvarsallPath}\" x64 && " +
               $"set \"VCPKG_ROOT={toolchain.VcpkgRoot}\" && " +
               "cmake --preset windows-x64-release && " +
               "cmake --build --preset windows-x64-release --target dovahlink_bridge_plugin";
    }
}
