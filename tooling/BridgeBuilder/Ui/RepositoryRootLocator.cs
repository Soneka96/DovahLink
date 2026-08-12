namespace DovahLink.BridgeBuilder.Ui;

public static class RepositoryRootLocator
{
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
