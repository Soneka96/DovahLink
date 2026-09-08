namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Describes the Visual Studio and vcpkg paths required by a build.</summary>
/// <param name="VcvarsallPath">The path to the Visual Studio environment script.</param>
/// <param name="VcpkgRoot">The bundled vcpkg root.</param>
public sealed record VisualStudioToolchain(string VcvarsallPath, string VcpkgRoot);
