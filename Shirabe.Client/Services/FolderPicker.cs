using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Shirabe.Client.Services;

/// <param name="getTopLevel">The window to show the dialog on, looked up when needed.</param>
public sealed class FolderPicker(Func<TopLevel?> getTopLevel) : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
        if (getTopLevel() is not { } topLevel) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Choose a music folder", AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
