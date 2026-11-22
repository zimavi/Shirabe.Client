namespace Shirabe.Client.Services;

/// <summary>Lets the user choose a folder. An interface so view models do not depend on the UI framework.</summary>
public interface IFolderPicker
{
    /// <returns>The chosen folder, or null if the user cancelled.</returns>
    Task<string?> PickFolderAsync();
}
