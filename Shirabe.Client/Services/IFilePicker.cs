namespace Shirabe.Client.Services;

/// <summary>Lets the user choose a file to read or a place to write one. An interface so view models do not depend on the UI framework.</summary>
public interface IFilePicker
{
    /// <param name="pattern">For example <c>*.json</c>.</param>
    /// <returns>The chosen file, or null if the user cancelled.</returns>
    Task<string?> PickOpenAsync(string title, string description, string pattern);

    /// <returns>Where to write, or null if the user cancelled. The system dialog asks before replacing a file.</returns>
    Task<string?> PickSaveAsync(string title, string suggestedName, string description, string pattern);
}
