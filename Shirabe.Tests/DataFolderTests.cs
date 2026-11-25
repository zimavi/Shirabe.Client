using System.Text.Json;

using Shirabe.Library;

using Xunit;

namespace Shirabe.Tests;

public sealed class DataFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shirabe-data-{Guid.NewGuid():N}");
    private readonly string _old;
    private readonly string _new;

    public DataFolderTests()
    {
        _old = Path.Combine(_root, "Shirabe");
        _new = Path.Combine(_root, "ShirabeData");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static readonly string[] Data =
    [
        "library.db", "library.db-wal", "library.db.bak-20261125000000", "settings.json", "session.json",
        Path.Combine("artwork", "cover.jpg"), Path.Combine("themes", "mine.json"),
    ];

    private static readonly string[] Installation =
        ["Update.exe", "Shirabe.exe", Path.Combine("current", "Shirabe.exe"), Path.Combine("packages", "a.nupkg")];

    private static void Write(string folder, string name, string? text = null)
    {
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text ?? name);
    }

    private static string[] FilesIn(string folder) => Directory.Exists(folder)
        ? Sorted(Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file)))
        : [];

    private static string[] Sorted(IEnumerable<string> names) => names.Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void All_the_data_moves_and_the_installation_stays()
    {
        foreach (var name in Data.Concat(Installation)) Write(_old, name);

        Assert.Equal(_new, DataFolder.MoveData(_old, _new));

        Assert.Equal(Sorted(Data), FilesIn(_new));
        Assert.Equal(Sorted(Installation), FilesIn(_old));
        Assert.All(Data, name => Assert.Equal(name, File.ReadAllText(Path.Combine(_new, name))));
    }

    [Fact]
    public void A_new_installation_has_nothing_to_move()
    {
        Assert.Equal(_new, DataFolder.MoveData(_old, _new)); // no old folder at all
        Assert.False(Directory.Exists(_new));

        foreach (var name in Installation) Write(_old, name);

        Assert.Equal(_new, DataFolder.MoveData(_old, _new));
        Assert.Empty(FilesIn(_new));
        Assert.Equal(Sorted(Installation), FilesIn(_old));
    }

    [Fact]
    public void Nothing_moves_once_the_new_folder_has_a_library()
    {
        // e.g. an older version was run again after the move and made itself a new, empty library
        Write(_new, "library.db", "mine");
        Write(_old, "library.db", "empty");
        Write(_old, "session.json");

        Assert.Equal(_new, DataFolder.MoveData(_old, _new));

        Assert.Equal("mine", File.ReadAllText(Path.Combine(_new, "library.db")));
        Assert.Equal(["library.db"], FilesIn(_new));
        Assert.Equal(["library.db", "session.json"], FilesIn(_old));
    }

    [Fact]
    public void An_interrupted_move_is_finished_without_overwriting_anything()
    {
        Write(_new, "settings.json", "already moved");
        Write(_old, "settings.json", "must not replace it");
        Write(_old, "library.db");

        Assert.Equal(_new, DataFolder.MoveData(_old, _new));

        Assert.Equal("already moved", File.ReadAllText(Path.Combine(_new, "settings.json")));
        Assert.True(File.Exists(Path.Combine(_new, "library.db")));
        Assert.Equal(["settings.json"], FilesIn(_old));
    }

    [Fact]
    public void A_library_in_use_leaves_everything_where_it_was()
    {
        // Only Windows refuses to rename an open file, and only Windows moves its data.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows file locking.");
        foreach (var name in Data) Write(_old, name);

        using (new FileStream(Path.Combine(_old, "library.db"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(_old, DataFolder.MoveData(_old, _new));
        }

        Assert.Equal(Sorted(Data), FilesIn(_old));
        Assert.Empty(FilesIn(_new));

        Assert.Equal(_new, DataFolder.MoveData(_old, _new)); // the next start
        Assert.Equal(Sorted(Data), FilesIn(_new));
    }

    [Fact]
    public void The_saved_session_finds_its_cover_in_the_new_folder()
    {
        // a name that JSON escapes, as a Windows user name can be
        var old = Path.Combine(_root, "Антон", "Shirabe");
        var moved = Path.Combine(_root, "Антон", "ShirabeData");
        Write(old, "library.db");
        Write(old, "session.json",
            JsonSerializer.Serialize(new { ArtworkPath = Path.Combine(old, "artwork", "cover.jpg"), Other = old }));

        DataFolder.MoveData(old, moved);

        using var session = JsonDocument.Parse(File.ReadAllText(Path.Combine(moved, "session.json")));
        Assert.Equal(Path.Combine(moved, "artwork", "cover.jpg"),
            session.RootElement.GetProperty("ArtworkPath").GetString());
        Assert.Equal(old, session.RootElement.GetProperty("Other").GetString()); // only cover paths are rewritten
    }
}