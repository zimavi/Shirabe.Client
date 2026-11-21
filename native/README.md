# Native BASS libraries

Shirabe plays audio through [BASS](https://www.un4seen.com/bass.html) by Un4seen Developments.
The native binaries are **not** stored in this repository. Download them yourself and place them here:

| Platform | Download | Put in |
|---|---|---|
| Windows x64 | `bass24.zip` -> `x64/bass.dll` | `native/win-x64/bass.dll` |
| Linux, macOS | not wired up yet | |

BASS is free for non-commercial use and requires a licence otherwise. Read the licensing section on
the BASS page before distributing builds. Use of BASS in this project is subject to those terms.
There must be no donation or supporter features inside the app.

Without the library the app starts and shows a "could not start the audio engine" message.
