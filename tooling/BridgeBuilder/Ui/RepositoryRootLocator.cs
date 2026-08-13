namespace DovahLink.BridgeBuilder.Ui;

public static class RepositoryRootLocator
{
    /// <summary>
    /// Locates the DovahLink repository root by searching upward from the specified path.
    /// </summary>
    /// <param name="startPath">The path from which to begin the search.</param>
    /// <returns>The first ancestor directory containing <c>bridge/vcpkg.json</c>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no repository root is found.</exception>
    public static string Find(string startPath)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "bridge", "vcpkg.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find the DovahLink repository. Keep this builder inside the repository or its tooling output folder.");
    }
}
