namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Describes the Creation Kit Papyrus compiler and source paths required by a build.</summary>
/// <param name="CompilerPath">The path to <c>PapyrusCompiler.exe</c>.</param>
/// <param name="ImportDirectory">The Papyrus source import directory passed to the compiler.</param>
/// <param name="FlagsFilePath">The path to <c>TESV_Papyrus_Flags.flg</c>.</param>
public sealed record PapyrusToolchain(string CompilerPath, string ImportDirectory, string FlagsFilePath);
