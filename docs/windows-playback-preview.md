# Windows playback preview verification

This procedure verifies the installed Windows x64 package. It uses the Windows Media Foundation components and decoders available on the machine. Record the Windows edition and version, the installed package variant, the audio device, and the monitor configuration alongside the results. Media Foundation and the corresponding decoders must be installed for playback. On editions that omit media components, verify the appropriate Windows media features before running the matrix.

## Generate the fixtures

Run the [fixture generator](../scripts/new-playback-fixtures.ps1) with an FFmpeg build that includes the AAC, libx264, and libmp3lame encoders.

```powershell
./scripts/new-playback-fixtures.ps1 -OutputDirectory ./artifacts/playback-fixtures
$env:BROADPLAY_PLAYBACK_FIXTURES = (Resolve-Path ./artifacts/playback-fixtures).Path
dotnet test Broiler.Broadplay.Tests.slnx -c Release
```

The fixtures consist of four seconds of synthesized tone and solid colors. The tone rises from 440 Hz to 880 Hz at 2 seconds. The video turns from red to blue at the same point. The script also generates a silent variant and makes byte-identical copies with misleading or missing extensions. It uses no third-party media files.

| File | Streams | Expected view and output |
| --- | --- | --- |
| `tone.wav` | WAVE PCM16, 48 kHz | Audio view, two pitches, waveform |
| `tone.mp3` | MP3, 48 kHz | Audio view, two pitches |
| `audio-only.mp4` | MP4 with AAC | Audio view, two pitches |
| `color-and-tone.mp4` | MP4 with H.264 and AAC | Video view, red then blue, two pitches |
| `silent-color.mp4` | MP4 with H.264 | Video view, red then blue, silence |
| `extensionless-tone` and `misnamed-video.m4a` | Copies of the WAVE and H.264/AAC fixtures | The same result as each original |
| `invalid.mp4` | Text | An error, with the previous session still available |

The fixture tests verify Windows stream routing, session readiness, and audio and video transport. The video test covers seek tolerance, reset on Stop, natural completion, replay, and asynchronous teardown. A separate test verifies aspect fit and surface restoration after a modal dialog. The clock and readiness also need independent output checks on the packaged app.

## Verify sound and displayed frames

1. Launch the packaged executable and use **Open media** to select each fixture. A fixture path can also be passed as the executable's first argument.
2. Press **Play**. Listen for the two pitches on the intended output device and look for the red-to-blue transition where expected. Record a separate outcome for audio and video.
3. Pause, seek to 1.5 seconds, and resume. Seek again to 2.5 seconds. Match the tone and frame color to the table and use a tolerance of 0.3 seconds for a settled seek position. The ended state must allow replay, and Stop should reset the position to zero.
4. Open a valid item during playback, then open an invalid item. Confirm the current item remains available after the failed load. Open several files quickly and confirm the latest selection wins. Close while opening and verify a late result cannot reopen the player.
5. With video playing, open a menu or dialog, resize the window, enter and exit fullscreen, and move between monitors with different scale factors. Video should fit the stage without changing its aspect ratio. The native video surface is hidden while Broiler menus or dialogs cover it. Caption overlays drawn over a native child window need a separate composition check before M5.
6. Switch the default Windows output or unplug the active headphones. When Windows signals an output change, Broadplay requests a pause. Confirm playback stays paused until an explicit **Play**. Check device loss, suspend and resume, display sleep, minimize and restore, and that closing the player releases the display request.

In-session volume and mute apply to both audio files and audio carried by video. Video volume applies to audio sessions owned by the Broadplay process and restores the original mixer settings on close. The Windows video head presents through MFPlay on a sibling HWND above the Direct2D client so Broiler can hide it while menus and dialogs cover the playback area.

The NativeAOT Windows desktop verification observed the expected red and blue frames and a Core Audio peak of 0.211 from the preview process for the generated H.264/AAC MP4. WAVE PCM, MP3, and audio-only AAC/MP4 also produced measured peaks of 0.200, 0.190, and 0.205 respectively. The 35% setting reached the active Windows audio session, and mute and unmute changed the Windows mute flag while keeping that level. Fullscreen filled the monitor and Escape restored the original bounds. A simulated suspend notification paused playback and exposed the recovery message. Physical device switching, an actual suspend and resume, and multi-monitor behavior still need verification before public release.

Release results should report session readiness separately from listening and visual inspection. Publish a codec combination as verified only after its expected output is observed from the package on the stated Windows configuration.

## Release handoff

The Windows 11 Enterprise desktop running build 26200 produced measured audio for WAVE PCM16, MP3, and AAC in MP4, and displayed H.264 in MP4 with AAC audio. The silent H.264 case and misleading-extension cases passed native session tests. Windows x64 NativeAOT and framework-dependent packages are built by the [publish workflow](../.github/workflows/publish.yml). Record any additional supported Windows and decoder configurations only after testing them.

Pending release checks are a physical headphone or default-output switch, a physical suspend and resume, and moving fullscreen video between monitors with different DPI. Use the procedure above and retain the results with the package. Then publish the Windows preview with the tested codec matrix.
