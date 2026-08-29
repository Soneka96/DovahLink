namespace DovahLink.Host;

// ---- Trust ----

/// <summary>Small, cross-cutting constant values shared across the host, grouped by area.</summary>
public static class Constants
{
    /// <summary>The default per-Windows-user file the trust store is persisted to.</summary>
    public static readonly string TrustStoreFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DovahLink",
        "host",
        "trust-store.dat");
}
