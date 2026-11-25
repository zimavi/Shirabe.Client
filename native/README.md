# Native BASS libraries

Shirabe plays audio through [BASS](https://www.un4seen.com/bass.html) by Un4seen Developments, and uses its
BASS_FX add-on (by JOBnik!) for keep pitch, reverb and the equaliser.
The native binaries are **not** stored in this repository. **The build downloads the ones for your platform**
from the BASS site the first time they are missing (`Shirabe.Audio` does it, `build/BassNative.props` copies them next to the
app):

| Platform          | Downloaded from                                                     | Put in                                       |
|-------------------|---------------------------------------------------------------------|----------------------------------------------|
| Windows x64       | `bass24.zip` -> `x64/bass.dll`                                      | `native/win-x64/bass.dll`                    |
| Linux x64 / arm64 | `bass24-linux.zip` -> `libs/x86_64` or `libs/aarch64` `/libbass.so` | `native/linux-x64/` or `native/linux-arm64/` |
| macOS             | `bass24-osx.zip` -> `libbass.dylib`                                 | `native/osx/libbass.dylib`                   |

BASS_FX comes from `https://www.un4seen.com/files/z/0/` and goes into the same folder:

| Platform          | Downloaded from                                                            | File               |
|-------------------|----------------------------------------------------------------------------|--------------------|
| Windows x64       | `bass_fx24.zip` -> `x64/bass_fx.dll`                                       | `bass_fx.dll`      |
| Linux x64 / arm64 | `bass_fx24-linux.zip` -> `libs/x86_64` or `libs/aarch64` `/libbass_fx.so` | `libbass_fx.so`    |
| macOS             | `bass_fx24-osx.zip` -> `libbass_fx.dylib`                                  | `libbass_fx.dylib` |

BASS_FX is free; its readme asks for credit (it is given on Settings > About, with SoundTouch, LGPL, and Freeverb,
which it contains). Without it the app plays normally and the three effects are greyed out.

If the download fails (offline, blocked) the build only warns; put the file there yourself. Delete it to get a newer
version.
Only the platform you build on is downloaded; to fetch another one, build with `-p:BassNativeRid=linux-x64` (or
`linux-arm64`, `osx`, `win-x64`).
Playback works with these on Windows, Linux and macOS.

BASS is free for non-commercial use and requires a licence otherwise. Read the licensing section on
the BASS page before distributing builds. Use of BASS in this project is subject to those terms.
There must be no donation or supporter features inside the app.

Without the library the app starts and shows a "could not start the audio engine" message.
