# Broiler.Broadplay

[![CI](https://github.com/Broiler-Platform/Broiler.Broadplay/actions/workflows/ci.yml/badge.svg)](https://github.com/Broiler-Platform/Broiler.Broadplay/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

Broiler Broadplay is an audio and video player built on the [Broiler](https://github.com/Broiler-Platform/Broiler)
managed-code application stack for .NET.

Use **Open media** or **Ctrl+O** to open a file. **Open folder** browses media candidates
and subfolders. Use [Broiler Plate](https://github.com/Broiler-Platform/Broiler.Plate)
for documents, standalone images, printing, and scanning.

## Getting started

Install the .NET 10 SDK. Package versions are pinned in [Directory.Packages.props](Directory.Packages.props).

| Head | Project | Playback |
| --- | --- | --- |
| Windows | `src/Broiler.Broadplay.Windows` | Media Foundation supplies live playback for streams supported by the installed codecs. |
| Linux | `src/Broiler.Broadplay.Linux` | Media inspection. Audio and video output are planned. |
| WebAssembly | `src/Broiler.Broadplay.WebAssembly` | Media inspection. Browser playback is planned. Building requires the wasm-tools workload. |

Run the Windows player with:

```bash
dotnet run --project src/Broiler.Broadplay.Windows/Broiler.Broadplay.Windows.csproj
```

## Player controls

The main view shows the selected file and its available playback controls. With focus in
the player, Space toggles play or pause, and Left and Right seek where available. The
position and volume sliders also support their own keyboard controls. **Playback** provides
Stop and other secondary commands. **F11** toggles fullscreen on Windows and Theater mode
on the other heads. **Escape** returns to the normal layout.

The audio and video filters identify candidate files. Container and codec support depends
on the playback backend. An inspection-only session shows file information with playback
controls disabled.

For Windows codec candidates and package verification, see the [Windows playback preview procedure](docs/windows-playback-preview.md).

## Queue and local collection

Open **Playback → Queue and collection** to manage the queue and saved playlists.
On desktop, **File → Select multiple files** opens the Library tab for batch selection. Browse
to a folder, select files, and choose an action. In the browser, **Open media** and drag and drop
accept a selection of files. **Play now** adds the selection at the end and starts its first
available item. **Play next** inserts after the current queue item. **Add to queue** appends without
interrupting playback. **Previous** restarts the current track after three seconds of playback,
or picks the previous item.

- In **Queue**, enter a local path or M3U/M3U8 location to import. Remote entries remain visible
  for later use. Export writes a new UTF-8 playlist file.
- In **Playlists**, enter a name and save the queue. Use **Load into queue** to replace the
  queue or **Append to queue** to keep its current entries. Updating or deleting a list only
  changes the collection.
- In **Folders**, opt into indexing selected folders. Refresh or cancel the background scan
  from the same tab. Library search includes loaded ID3 title, artist, and album tags.
  WAVE files can show duration, and available ID3 cover artwork appears in the folder gallery.
- In **Recent**, turn on position resume or clear history. The queue and saved lists restore
  without autoplay. For a missing item, select **Relink** and enter the new local path on
  desktop, or reselect the file in the browser. Browser file data remains available for the page
  session. A browser reload can require reselection.

To add the installed Windows executable to **Open with**, run
`./scripts/register-open-with.ps1 -Executable <path-to-executable>`. On Linux, run
`sh scripts/install-broadplay-desktop.sh /absolute/path/to/Broiler.Broadplay.Linux`.
Both registrations add Broadplay as a choice and preserve the current default handlers.

## About

*Help ▸ About Broiler Broadplay* opens the standard Broiler About dialog with the product
version and component versions. The product version comes from `Broiler.Broadplay.Core`,
preserves the prerelease label, and omits the build commit hash. Enter or Escape closes
the dialog and returns focus to the player.

The default version is `0.1.0-preview.1`, shared by Broadplay's projects through
`Directory.Build.props`. Override it at build or publish time with
`-p:BroilerBroadplayVersion=0.1.0-preview.2`.

## Solutions

`.slnx` files are generated, never hand-edited. Declare the entry point in
[`eng/solutions.json`](eng/solutions.json) and run:

```bash
pwsh scripts/update-solutions.ps1
```

`-Verify` fails instead of writing, which is what CI should run.

| Solution | Contents |
|---|---|
| `Broiler.Windows.Broadplay.slnx` | Windows head and shared player core (2 projects) |
| Broiler.Linux.Broadplay.slnx | Linux head and shared player core |
| Broiler.WebAssembly.Broadplay.slnx | Browser head and shared player core |
| `Broiler.Broadplay.Tests.slnx` | Windows tests, head and shared player core (3 projects) |

## Continuous integration

[`ci.yml`](.github/workflows/ci.yml) runs four jobs on push, pull request and manual
dispatch:

| Job | Runner | What it protects |
|---|---|---|
| Solution manifest | `ubuntu-latest` | The checked-in `.slnx` still matches the real reference graph. Catches a hand-edit, and the subtler case of adding a `ProjectReference` without regenerating — which leaves the new project out of the solution, so it is never built and never seen to break. |
| Windows head | `windows-latest` | `dotnet build -c Release` of the whole solution. |
| Tests | `windows-latest` | Shared player behavior, native audio session opening, and About dialog behavior through `dotnet test -c Release`. |
| Publish win-x64 (NativeAOT) | `windows-latest` | `Release-Windows` — a configuration the solution does not declare, so nothing else exercises it — published with NativeAOT, then started to check it stays up. |

The publish job earns its place twice. `Release-Windows` can only be built at project level (a
solution-level build with an undeclared configuration fails `MSB4126`), so it is the only
thing standing between a `Directory.Build.props` regression and a release built unoptimized
with neither `RELEASE` nor `WINDOWS` defined. And NativeAOT is the one build mode that fails
on reflection every other mode accepts: adding `Activator.CreateInstance` or reflection-based
serialization to the head or to `Broiler.Broadplay.Core` breaks it while leaving an ordinary build
green. `PublishAot` is passed on the command line, never set in a `.csproj`, so day-to-day
builds stay framework-dependent and fast.

The setup action installs .NET 10; every package restores from nuget.org, so no feed
credentials are involved. Only the publish workflow's release job may write (to push its tag
and draft the release); no submodule checkout is required.

[`publish.yml`](.github/workflows/publish.yml) is dispatch-only and builds the Windows Broadplay
in two variants, each a zip artifact on the run:

- `broiler-broadplay-win-x64-self-contained-<version>`: NativeAOT, a single executable,
  `Broiler.Broadplay.Windows.exe`, which is the whole application: no .NET runtime to install and
  nothing beside it. The run fails if the publish ever emits anything beside the executable
  other than symbols or XML docs, because an artifact without that file would be broken.
- `broiler-broadplay-win-x64-framework-dependent-<version>`: the IL build with its assemblies,
  for machines with the .NET 10 runtime installed. The run fails if it is missing its
  runtimeconfig or carries a runtime of its own.

Both are started to check they stay up. Nothing is pushed to a feed.

Each run also drafts a GitHub pre-release, *Broiler Broadplay <version>*, carrying
`Broiler.Broadplay-<version>-win-x64-self-contained.zip` and
`Broiler.Broadplay-<version>-win-x64-framework-dependent.zip`. It stays a draft until someone
publishes it under Releases; [`eng/release-draft.sh`](eng/release-draft.sh) builds it and can
be run by hand from a run's artifacts.

Its one input is an optional `version-suffix` such as `preview.7`.

Each run takes the next preview version: `BroilerBroadplayVersion` in `Directory.Build.props` is
the floor, raised past every earlier run's `broadplay-v*` tag. A run tags its commit only after
the build succeeded, so preview numbers only ever increase and a failed run leaves its number
free. The version is stamped into the build, so the About dialog and the file version match
the release. The logic is [`eng/resolve-preview-version.mjs`](eng/resolve-preview-version.mjs),
with tests beside it.

## Repository layout

| Path | Contents |
|---|---|
| `src/Broiler.Broadplay` | Shared application (`Broiler.Broadplay.Core`) — window, menu, toolbar, the player views, media intake, folder browser, and palette |
| `src/Broiler.Broadplay.Windows` | Windows head — `WinExe`, Direct2D, the Win32 clipboard from the `Broiler.Hosting.Windows` package, and the break-out host that gives each dialog its own OS window |
| `eng/`, `scripts/` | Solution manifest and generator, preview version resolver, release drafting |
| `.github/` | CI and release workflows, and the `setup-broiler` composite action |
| `Directory.Build.props` | Product version and configuration decomposition |

## Build configuration

The head declares four configurations. `Debug` and `Release` build framework-dependent;
`Debug-Windows` and `Release-Windows` pin `win-x64`. MSBuild only understands `Debug` and
`Release` on its own, so `Directory.Build.props` decomposes the compound names into a base
configuration and a target OS. Without it, `-c Release-Windows` would build unoptimized and
with neither `RELEASE` nor `WINDOWS` defined.

## Dependencies

NuGet package versions are managed centrally in
[`Directory.Packages.props`](Directory.Packages.props).

The platform hosting glue — the Win32 clipboard and window sizing — comes from the
`Broiler.Hosting.Windows` package, the same one the other Broiler applications use.

The application consumes the following components as NuGet packages, directly or
transitively:

| Component | Purpose |
|---|---|
| `Broiler.DOM` | Canonical DOM, HTML tokenization, parsing, traversal, serialization |
| `Broiler.Graphics` | Managed bitmap/codec/raster core plus platform backends |
| `Broiler.Hosting` | Platform hosting glue, clipboard, and window sizing |
| `Broiler.Media` | Image, audio and video abstractions and managed codecs |
| `Broiler.Input` | Keyboard, mouse, pen, touch and text input abstractions |
| `Broiler.UI` | Platform-neutral retained-mode UI toolkit |

## Roadmap

The [Broadplay media player roadmap](docs/roadmap.md) covers the transition from the
Plate-derived viewer to a dedicated audio and video player on Windows, Linux, and WebAssembly.

## License

Apache License 2.0 — see [LICENSE](LICENSE).
