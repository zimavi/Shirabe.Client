namespace Shirabe.Client.Services;

/// <summary>How the Albums page is ordered. The names are saved in settings.json: do not rename them.</summary>
public enum AlbumSort
{
    Title,

    /// <summary>By album artist, each artist's albums oldest first.</summary>
    Artist,

    /// <summary>Newest first.</summary>
    Year,
}