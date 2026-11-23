# Native BASS libraries

Shirabe plays audio through [BASS](https://www.un4seen.com/bass.html) by Un4seen Developments.
The native binaries are **not** stored in this repository. **The build downloads the one for your platform**
from the BASS site the first time it is missing (`Shirabe.Audio` does it, `build/BassNative.props` copies it next to the
app):

| Platform          | Downloaded from                                                     | Put in                                       |
|-------------------|---------------------------------------------------------------------|----------------------------------------------|
| Windows x64       | `bass24.zip` -> `x64/bass.dll`                                      | `native/win-x64/bass.dll`                    |
| Linux x64 / arm64 | `bass24-linux.zip` -> `libs/x86_64` or `libs/aarch64` `/libbass.so` | `native/linux-x64/` or `native/linux-arm64/` |
| macOS             | `bass24-osx.zip` -> `libbass.dylib`                                 | `native/osx/libbass.dylib`                   |

If the download fails (offline, blocked) the build only warns; put the file there yourself. Delete it to get a newer
version.
Only the platform you build on is downloaded; to fetch another one, build with `-p:BassNativeRid=linux-x64` (or
`linux-arm64`, `osx`, `win-x64`).
Only the Windows build has been run with the app; the Linux and macOS files are fetched and unpacked correctly (checked)
but not played.

BASS is free for non-commercial use and requires a licence otherwise. Read the licensing section on
the BASS page before distributing builds. Use of BASS in this project is subject to those terms.
There must be no donation or supporter features inside the app.

Without the library the app starts and shows a "could not start the audio engine" message.
