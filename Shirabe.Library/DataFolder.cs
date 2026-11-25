using System.Text.Json;

namespace Shirabe.Library;

/// <summary>The folder of the user's data: the library database, settings, session, covers and themes.</summary>
public static class DataFolder
{
    private const string Database = "library.db";

    /// <summary>
    /// The data folder on this system. On Windows, data still in the old place is moved first, so call this before
    /// anything opens a file in it.
    /// </summary>
    public static string Locate()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var shared = Path.Combine(local, "Shirabe");

        // Windows: "Shirabe" is where Velopack installs the app, and uninstalling or running an installer again deletes
        // that folder with everything in it. Nothing of the user's may live there. Elsewhere the two are separate.
        return OperatingSystem.IsWindows() ? MoveData(shared, Path.Combine(local, "ShirabeData")) : shared;
    }

    /// <summary>
    /// Moves Shirabe's data, and nothing else, from one folder to another. All of it moves or none of it: if something
    /// is in use, what has moved goes back and the next start tries again. Nothing is ever overwritten, and nothing
    /// happens once <paramref name="to"/> has a library.
    /// </summary>
    /// <returns><paramref name="to"/>, or <paramref name="from"/> when the data could not be moved and is still there.</returns>
    public static string MoveData(string from, string to)
    {
        if (!Directory.Exists(from) || File.Exists(Path.Combine(to, Database))) return to;

        var moved = new Stack<(string Source, string Target)>();
        try
        {
            Directory.CreateDirectory(to);

            // The database goes last, after its journal and backups: until it has arrived, a start that follows an
            // interrupted move carries on with what is left.
            var data = new[] { "artwork", "themes", "settings.json", "session.json" }
                .Select(name => Path.Combine(from, name))
                .Where(Path.Exists)
                .Concat(Directory.GetFiles(from, Database + "*").OrderBy(file => Path.GetFileName(file) == Database))
                .ToList();

            foreach (var source in data)
            {
                var target = Path.Combine(to, Path.GetFileName(source));
                if (Path.Exists(target)) continue;

                Directory.Move(source, target); // moves files as well as folders
                moved.Push((source, target));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            while (moved.TryPop(out var item))
            {
                try
                {
                    Directory.Move(item.Target, item.Source);
                }
                catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
                {
                    // Stays in the new folder; the rest still goes back.
                }
            }

            return from;
        }

        PointSessionAtMovedCovers(from, to);
        return to;
    }

    // session.json remembers the last song with the full path of its cover.
    private static void PointSessionAtMovedCovers(string from, string to)
    {
        var session = Path.Combine(to, "session.json");
        try
        {
            if (!File.Exists(session)) return;

            // As the path appears inside the JSON text: escaped, without the quotes.
            static string InJson(string folder) =>
                JsonSerializer.Serialize(Path.Combine(folder, "artwork") + Path.DirectorySeparatorChar)[1..^1];

            var text = File.ReadAllText(session);
            File.WriteAllText(session, text.Replace(InJson(from), InJson(to), StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the restored song's cover is at stake.
        }
    }
}