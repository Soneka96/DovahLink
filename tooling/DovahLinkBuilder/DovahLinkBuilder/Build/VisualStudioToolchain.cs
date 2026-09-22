namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Describes the Visual Studio tools and bundled vcpkg paths required by a build.</summary>
/// <param name="VcvarsallPath">The path to the Visual Studio environment script.</param>
/// <param name="VcpkgRoot">The bundled vcpkg root.</param>
/// <param name="CMakePath">The CMake executable bundled with that Visual Studio installation.</param>
/// <param name="NinjaPath">The Ninja executable bundled with that Visual Studio installation.</param>
public sealed record VisualStudioToolchain(string VcvarsallPath, string VcpkgRoot, string CMakePath, string NinjaPath);
