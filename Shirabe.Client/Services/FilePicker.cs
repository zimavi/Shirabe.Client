using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Shirabe.Client.Services;

/// <param name="getTopLevel">The window to show the dialog on, looked up when needed.</param>
public sealed class FilePicker(Func<TopLevel?> getTopLevel) : IFilePicker
{
    public async Task<string?> PickOpenAsync(string title, string description, string pattern)
    {
        if (getTopLevel() is not { } topLevel) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(description) { Patterns = [pattern] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveAsync(string title, string suggestedName, string description, string pattern)
    {
        if (getTopLevel() is not { } topLevel) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(description) { Patterns = [pattern] }],
        });
        return file?.TryGetLocalPath();
    }
}
