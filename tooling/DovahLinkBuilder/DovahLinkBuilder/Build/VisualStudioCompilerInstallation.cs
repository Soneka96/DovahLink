namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>
/// Describes a candidate installation root that contains a Visual Studio compiler environment,
/// independent of whether its bundled CMake, Ninja, or vcpkg are also present.
/// </summary>
/// <param name="Root">The candidate installation root.</param>
/// <param name="VcvarsallPath">The environment script found under <paramref name="Root"/>.</param>
public sealed record VisualStudioCompilerInstallation(string Root, string VcvarsallPath);
