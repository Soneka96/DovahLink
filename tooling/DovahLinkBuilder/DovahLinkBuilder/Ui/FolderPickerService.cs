using System.IO;
using Microsoft.Win32;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Prompts the user to pick a folder through a native OS dialog.</summary>
public interface IFolderPickerService
{
    /// <summary>Shows a folder-picker dialog and returns the chosen folder.</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="initialDirectory">The folder the dialog opens to, when it still exists; otherwise the dialog's own default.</param>
    /// <returns>The chosen folder's full path, or <see langword="null"/> when the user cancelled.</returns>
    string? PickFolder(string title, string? initialDirectory);
}

/// <summary>Prompts the user to pick a folder through Windows' native folder-picker dialog.</summary>
public sealed class FolderPickerService : IFolderPickerService
{
    /// <inheritdoc/>
    public string? PickFolder(string title, string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (initialDirectory is not null && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
