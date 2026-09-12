namespace DovahLink.Host;

/// <summary>
/// The raw, unvalidated shape of the user-editable settings file on disk. Deserialized directly by
/// <see cref="HostSettingsProvider"/>; every field is optional and may be missing, out of range, or
/// otherwise invalid, since a hand-edited file is untrusted input.
/// </summary>
internal sealed class HostSettingsFile
{
    /// <summary>The raw <c>maxActiveSessions</c> field, or <see langword="null"/> when absent from the file.</summary>
    public int? MaxActiveSessions { get; set; }
}
