# Shirabe (シラベ)

A music player for the music you already have, built around one idea: **music deserves more than a play button.**

Most players show you a title, an artist and a cover. Shirabe wants to know more about a song than that — who played
what,
what the words are, what the artist wanted to say — and show it while you listen, without getting in the way.
It works fully offline on your own files. No account, no server.

> Shirabe is early and changing quickly. It works and I use it, but expect rough edges.
> It has only been run on Windows so far.

## What it does today

- **Plays your music.** Add a folder, Shirabe scans it and keeps a library. Queue, shuffle, repeat, volume, and it
  remembers where you left off.
- **Lyrics.** Open the lyrics panel and follow along. Synced lyrics highlight the current line and fill word by word,
  like karaoke.
  It reads `.lrc` and `.ttml` files (including the ones [Better Lyrics](https://composer.betterlyrics.org/) makes), or
  lyrics already inside the file's tags.
  Duets, background vocals and breaks between lines are shown too. Click a line to jump to it.
- **Song details.** Right-click a song and choose Properties: the tags in the file, and your own custom fields.
- **Studio.** A place to write and check richer song information in the *Shirabe Metadata Format* (a small open JSON
  format, drafted in this project).
  Save it as a file next to the song, or into the song's own tags when you ask for it.
- **Looks.** Themes (you can add your own), an accent colour that follows the cover, and a reduce-motion switch.
- **Discord.** Shows what you are listening to on your Discord profile, if you want it. There is a switch in the
  settings.

Not there yet: albums, artists and playlists pages, a backend, and anything online beyond the optional cover lookup and
covers that a song's own metadata links to.

## Try it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project Shirabe.Client.Desktop
```

The first build downloads the audio library (BASS) for your platform from its website, so it needs a connection once.
If that fails, see [native/README.md](native/README.md).

Run the tests with:

```
dotnet test
```

## Lyrics: how to add them

Put a file with the **same name as the song** next to it:

```
Music/
  Some Song.flac
  Some Song.ttml      <- or Some Song.lrc
```

or put lyrics in the song's tags. Timed lyrics win over plain text, and a `.ttml` file wins over `.lrc`.
You can tune the text size, background vocals, break markers and timing in **Settings → Lyrics**.

## Your data

- Your library and settings live in `%LOCALAPPDATA%\Shirabe` (the library is `library.db`, a normal SQLite file).
- Shirabe does not change your audio files, with one exception: if you choose **Write into the audio file** in Studio,
  it adds one tag (`SHIRABE_METADATA`) after asking you, keeps a copy while it works, and puts the original back if
  anything looks wrong.
- Your own notes and fields are never overwritten by a rescan or by anything that comes from outside.

## How it is put together

| Folder                   | What is in it                                                                     |
|--------------------------|-----------------------------------------------------------------------------------|
| `Shirabe.Client`         | The app's screens and view models (Avalonia)                                      |
| `Shirabe.Client.Desktop` | The desktop program that starts it                                                |
| `Shirabe.Core`           | The small interfaces everything else shares (playback, library, lyrics, metadata) |
| `Shirabe.Audio`          | Playback, using BASS                                                              |
| `Shirabe.Library`        | The local library: SQLite, scanning, reading tags                                 |
| `Shirabe.Metadata`       | The metadata format: reading, checking and writing documents                      |
| `Shirabe.Tests`          | Tests                                                                             |

`CLAUDE.md` has the longer story of where the project is meant to go.

## Licence

Shirabe is under the [Apache License 2.0](LICENSE.txt).

The audio engine, [BASS](https://www.un4seen.com/), is **not** part of that licence and is not stored in this
repository.
It is free for non-commercial use and needs a licence from its authors otherwise — see [NOTICE.txt](NOTICE.txt)
and [native/README.md](native/README.md).

Built with [Avalonia](https://avaloniaui.net/), [ManagedBass](https://github.com/ManagedBass/ManagedBass),
[ATL](https://github.com/Zeugma440/atldotnet) and [Entity Framework Core](https://learn.microsoft.com/ef/core/).
