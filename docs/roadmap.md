# Broiler.Broadplay media player roadmap

Broadplay should open directly into an audio and video player. Users should be able to play a file, see what is playing, control playback, and decide what plays next. Plate remains the Broiler application for document reading, printing, scanning, and standalone image viewing. Broadplay uses images for artwork and previews.

## How to use this backlog

Each checkbox is a candidate issue. Keep its ID and acceptance check when filing it. Explicit dependencies identify work that must land first. Checked boxes record implemented and verified tasks. Milestone exit criteria determine release readiness.

| Milestone | Release outcome | Order |
| --- | --- | --- |
| M1. Player-first shell | Broadplay has a clear audio and video workflow and a clean boundary with Plate. | Start now. |
| M2. Windows preview | The packaged Windows app reliably plays a verified set of local media. | Start the session contract and fixtures alongside M1. |
| M3. Queue and collection | Users can build, play, save, and restore a local listening or viewing session. | Develop the queue model alongside M2. Wire auto-advance after session events are reliable. |
| M4. Linux and browser | Each additional head ships real playback with a published support matrix. | Start backend prototypes after the shared source and session contracts are drafted. Release each head independently. |
| M5. Full-featured playback | Supported heads gain tracks, captions, long-form playback, and network sources. | Build on the capabilities and release evidence of the target head. |
| Later candidates | Optional improvements with explicit discovery criteria. | Prioritize after observing the released workflows. |

The first implementation batch is BP-101, BP-102, BP-110, BP-201, and BP-202. These establish the migration boundary, player UI, honest capabilities, shared contracts, and a real playback baseline.

## Starting point

| Area | Current implementation | Implication |
| --- | --- | --- |
| Shared shell | Playback views sit alongside document reading, notes, print, scan, zoom, and a mixed-file gallery. | Keep useful shell controls and the thumbnail cache. Retire document-specific paths from Broadplay. |
| Windows | Media Foundation provides audio and video. Video uses a native child window and can fall back to a timeline preview if that surface is unavailable. The shared transport has play/pause, stop, seek, mute, and volume, with waveform data for analyzed WAVE files. | Prove real output and backend lifecycle behavior in the packaged app. |
| Linux and WebAssembly | Both use the managed timeline fallback without audio or video output. Browser local-file and folder ingestion currently reads file bytes. | Add capability-accurate UI now and live output per head later. |
| Format detection and tests | Signature and extension checks route requests. Current media tests include a fake timeline and opening a Windows WAVE session. | Validate actual streams with the backend and verify output separately from routing. |

Baseline source locations include [BroadplayApp](../src/Broiler.Broadplay/BroadplayApp.cs), [BroadplayMediaView](../src/Broiler.Broadplay/BroadplayMediaView.cs), [the shared session contracts](../src/Broiler.Broadplay/BroadplayMediaHost.cs), [format routing](../src/Broiler.Broadplay/BroadplayMediaFormats.cs), [the Windows media host](../src/Broiler.Broadplay.Windows/WindowsMediaHost.cs), [the Linux runner](../src/Broiler.Broadplay.Linux/LinuxBroadplayRunner.cs), [the browser bridge](../src/Broiler.Broadplay.WebAssembly/wwwroot/main.js), and [media tests](../src/tests/Broiler.Broadplay.Windows.Tests/BroadplayMediaTests.cs).

## M1. Separate Broadplay from Plate

Windows routes the file and folder pickers through the shared app and supplies Media Foundation. Linux routes the same pickers and startup paths through the shared app. WebAssembly routes file picker and drag-and-drop bytes through its media bridge, and folder selections through the shared media browser. Picker controls and thumbnail decoding remain shared. Plate retains the removed document, PDF, print, scan, and standalone image workflows.

The transport exposes named buttons and sliders to Windows UI Automation. A manual screen-reader check remains for BP-104. F11 and Escape control Theater mode on Linux and WebAssembly and native fullscreen on Windows.

### Playback workspace

- [x] **BP-101 Inventory commands, entry points, and dependencies.** Trace Windows, Linux, WebAssembly, and any existing embedding hosts through startup, file picker, folder picker, paths, and browser bytes. Identify the callers, tests, direct packages, and help text for document open, print, scan, notes, and zoom. **Accept when** each workflow has a named replacement, removal, or retained shared dependency and there is a smoke path for each head.
- [x] **BP-102 Lay out the player shell.** Provide **Open media** and **Open folder** in the empty state, a central area for audio artwork or video, and one transport area. Route launch and close to the same player empty state. Preserve a location for the upcoming queue. **Accept when** empty, loading, audio, video, and error states stay usable in a narrow window and at high DPI.
- [x] **BP-103 Surface item identity and playback state.** Use title and artist when available, a filename fallback, elapsed time, duration, and an artwork placeholder. Show opening, ready, ended, unsupported, and failed states with an appropriate action. **Accept when** missing tags and unknown duration have readable fallbacks and a failed open identifies the failed item while the previous playable selection remains available.
- [ ] **BP-104 Make transport controls individually accessible.** Use focusable controls or expose equivalent accessibility semantics for play/pause, seek, mute, and volume. Define names, roles, states, values, focus order, high-contrast treatment, and touch hit targets. **Accept when** keyboard and screen-reader users can identify and operate each control on the preview head, with equivalent semantics carried into BP-403 and BP-413.
- [x] **BP-105 Route shortcuts through the same commands as the transport.** Define Space, Left/Right, Escape, and fullscreen behavior with explicit focus rules. **Accept when** focused text fields and menus keep their normal keys, a slider receives its own arrow events, and buttons, menus, and shortcuts agree on enabled state.

### Remove the document workflow

- [x] **BP-106 Filter intake and folder browsing for media.** Update pickers, path handling, and the existing gallery to surface navigable folders and audio/video candidates. Treat document and image opens into Broadplay as unsupported and point to Plate. **Accept when** supported files can be found in a mixed folder and direct document opens return a clear message.
- [x] **BP-107 Remove document commands and views.** After BP-101 and BP-106, remove the RichEdit document view, document and standalone image zoom, document notes, print and scan UI, and their shortcuts. **Accept when** no document workflow is reachable in Broadplay and smoke checks confirm the corresponding workflows still run in Plate.
- [x] **BP-108 Remove clone-specific registrations and references.** Remove document codec registrations, PDF permission hooks, obsolete printing and scanning adapters, dead tests, and direct references used only by deleted paths. Keep the artwork decoding path and verify callers before removing constructor parameters. **Accept when** all head solutions build and Broadplay's direct dependency list matches its remaining features.
- [x] **BP-109 Update product identity and help.** Audit title bars, menu labels, tooltips, icons, About, platform manifests, README, and help. Update the browser picker's default filter, viewer labels, and document-named intake bridge while preserving launcher integration. Document current runtime support per head. **Accept when** every entry point identifies Broadplay as an audio/video player and documented commands match their behavior.
- [x] **BP-110 Distinguish inspection from playback.** Use the backend's actual output capability to gate playback commands. Keep simulated timelines available to state tests and label any user-visible inspection. **Accept when** a head without live output and a Windows video-surface failure cannot be presented as successful audio or video playback.

**Exit criteria.** Broadplay opens into a media-focused shell. Audio/video intake and basic keyboard workflows are reachable, the three heads build, and Plate retains its document workflows.

## M2. Ship the Windows local playback preview

Local builds and Media Foundation readiness are tracked separately from published support. The [Windows playback preview procedure](windows-playback-preview.md) covers generated fixtures, independent audio and video checks, and the desktop lifecycle. Browser-owned handle support follows in M4. The packaged H.264/AAC fixture produced both expected desktop frame colors and a measurable audio signal from the preview process.

### Shared playback contract

- [x] **BP-201 Model local media sources and ownership.** Distinguish local paths from byte-backed sources. The caller owns the bytes and the Windows host owns any temporary materialization. Local paths can be reopened without buffering the entire file. Backend-probed streams determine playback kind. Browser-owned handles are tracked in BP-412.
- [x] **BP-202 Establish decodable fixtures and output checks.** Start with WAVE PCM, MP3, and MP4 with H.264/AAC as candidates. Record fixture provenance, codecs, durations, recognizable seek markers, and expected output. **Accept when** a packaged-app test or repeatable manual procedure verifies sound and presented frames independently of the transport clock.
- [x] **BP-203 Define session snapshots, events, and capabilities.** Model loading, ready, playing, paused, seeking, ended, and failed. Represent unknown duration, seekability, and supported controls explicitly. Deliver state changes on the UI dispatcher even when playback ticks are idle. **Accept when** fake-host tests cover delayed readiness, failure while paused, duplicate or stale notifications, and deterministic control availability.
- [x] **BP-204 Open asynchronously and commit the latest ready session.** After BP-203, keep the current session until the replacement is ready, allow cancellation, and discard stale results. **Accept when** a delayed B cannot replace C after C succeeds, a failed B leaves A playing, and closing during load cleans up any late result.
- [x] **BP-205 Map backend errors to user actions.** Distinguish missing or unreadable files, missing decoders, unavailable output, and mid-playback failure. Supply retry, choose another file, or a relevant next step. **Accept when** errors arriving before or after readiness update the correct item and include enough information to troubleshoot.
- [x] **BP-206 Use probed streams for format routing.** Inspect the backend's audio and video streams before settling on the playback view. Treat extension and signature as hints, including for containers shared by audio and video. **Accept when** extensionless media, misleading extensions, audio-only MP4, silent video, and unsupported codecs produce the expected result.

### Windows output and transport

- [x] **BP-207 Adapt Media Foundation to the session contract.** Opens and transport commands run off the UI path. Teardown releases sessions, child surfaces, and temporary byte sources. Tests cover replacement, cancellation, close, failure to create a video surface, and completion of native video teardown.
- [x] **BP-208 Finish seek, stop, end, and replay.** Clamp absolute and relative seeks, coalesce video scrubbing, release input capture on Windows cancellation, and discard queued scrubs on Stop. Automated audio and video tests verify seek, stop at zero, natural completion, and replay.
- [x] **BP-209 Implement volume and audio-output policy.** Menu and transport share the session controls. Mute preserves the selected volume, Windows output-change notifications request a pause, and output failures offer recovery. Physical device switching is part of BP-212.
- [x] **BP-210 Implement video composition and fullscreen.** MFPlay presents through a sibling HWND above Direct2D. Keep the transport outside the video area and hide the surface during menus and modal dialogs. Automated checks cover aspect fit and modal restoration. A caption overlay prototype belongs with the composition work in BP-502. Multi-monitor and DPI verification is part of BP-212.
- [x] **BP-211 Implement desktop lifecycle policy.** Suspend requests a pause, and resuming requires explicit Play. Minimization, pausing, stopping, and closing release the display request. The simulated suspend check and state tests cover the policy. Actual suspend and resume is part of BP-212.
- [ ] **BP-212 Complete live release verification and publish the Windows preview.** The local package, automated checks, tested codec combinations, and OS prerequisites are recorded in the preview procedure. Complete the physical audio-device switch, suspend and resume, and multi-monitor and DPI checks on the release candidate. Publish the preview and its verified support matrix.

**Exit criteria.** A packaged Windows build audibly plays a verified audio fixture and visibly presents a video fixture. Seeking, replay, errors, replacement, fullscreen, and teardown pass the documented checks. The release states which combinations were verified.

## M3. Add the queue, playlists, and local collection

Implementation and automated validation are complete. The shared **Queue and collection** dialog exposes queue actions, named playlists, opt-in indexing, and resume controls. Windows accepts multiple startup paths and dropped files. Linux accepts multiple startup paths and provides an opt-in desktop MIME handler. Browser batch selection and drop use page-session files and preserve collection state locally. Previously selected browser files may need reselection after reload. Library metadata covers ID3v2 text and cover art and WAVE duration. Platform entry-point registration is opt-in and was checked without changing live desktop associations.

### Queue and intake

- [x] **BP-301 Create the shared queue model.** Give entries stable IDs, including duplicate references to the same file. Define add, move, remove, clear, and the result of removing the current item. **Accept when** edits around the active entry preserve its identity and duplicates can be moved or deleted independently.
- [x] **BP-302 Add queue playback policies.** After BP-203 and BP-301, implement previous/next, previous-track restart behavior, automatic advance, repeat off/one/all, and shuffle. Deduplicate completion events and bound failed-item traversal. **Accept when** duplicate EOF advances once, manual stop leaves the queue in place, and an all-failed queue stops attempting playback even with repeat enabled.
- [x] **BP-303 Add the queue panel and explicit intake actions.** Provide **Play now**, **Play next**, and **Add to queue** with documented behavior for an existing queue. Support selecting, reordering, and removing items with keyboard and pointer. **Accept when** each action produces a predictable order without restarting the current item unless playback was explicitly requested.
- [x] **BP-304 Route all file and folder entry points through intake.** Add multi-select, reuse browser drag and drop, and add desktop drop where supported. Route desktop startup paths, files opened from a file manager, and library actions into the same command path. **Accept when** a batch with spaces, Unicode names, and mixed file types preserves ordering and reports individual failures, and canceling a picker leaves the current state intact.
- [x] **BP-305 Import and export local M3U and M3U8 playlists.** Resolve relative entries against the playlist's directory, preserve order and duplicates, and report unsupported and missing entries. Treat remote entries as pending user action and distinguish HLS from a local playlist. **Accept when** UTF-8 paths round-trip and playlist import leaves the network idle.
- [x] **BP-306 Save named playlists separately from the active queue.** Provide create, rename, update, and delete, with explicit wording for loading versus appending. **Accept when** edits to the current queue affect a saved list only on an explicit save and deleting a list leaves its media files intact.

### Collection and persistence

- [x] **BP-307 Browse local media with useful metadata.** Provide search and natural sort, filename fallback, duration, album/artist tags, and artwork or video thumbnails. Respect disc and track order. **Accept when** a large mixed folder remains usable while metadata loads, stale results are discarded on navigation, and the cache stays bounded.
- [x] **BP-308 Add opt-in folder indexing.** Separate indexed folders from ad hoc folder browsing. Support refresh, cancellation, and removal from the index. **Accept when** additions, changes, and deletions appear after refresh and the app remains responsive during playback.
- [x] **BP-309 Persist local state and recover it safely.** Version the stored schema for queue order, playlists, recent items, volume, and preferences. Handle interrupted writes, damaged state, and upgrades. **Accept when** restart restores valid state without starting audio and corrupt state yields a recoverable default.
- [x] **BP-310 Resume long media and recover missing sources.** Add optional resume, start over, mark played, clear history, and relink or reselect. Define how completion and source replacement affect saved position. **Accept when** a moved file can be relinked, a completed item starts cleanly, and disabling history stops collection of new progress.
- [x] **BP-311 Add packaged desktop entry points.** After BP-304, register Open With or desktop MIME integration where supported, and validate any Broadplay launcher entry. **Accept when** a user can open a supported file via the installed app from the file manager, with existing OS default handlers preserved.

**Exit criteria.** A user can open a folder, build and edit a queue, and let it advance. Saved lists and supported queue entries survive a restart. Missing items can be relinked, and indexing leaves playback responsive.

## M4. Deliver real Linux and browser playback

### Linux

- [ ] **BP-401 Select a Linux backend.** Prototype both a system multimedia integration and a bundled option after BP-201 and the draft of BP-203. Compare actual audio and video output, codec coverage, acceleration, supported displays, licensing, native dependencies, and a packaged NativeAOT build. **Accept when** the chosen approach ships in a minimal package that plays the reference fixtures and the tradeoffs are recorded.
- [ ] **BP-402 Implement Linux session and output adapters.** After BP-401, map native events, errors, device changes, and video surface lifetime into the shared contract. **Accept when** real playback passes the shared state checks and release fixtures on the intended configurations.
- [ ] **BP-403 Verify Linux desktop integration.** Exercise fullscreen, resizing, keyboard and assistive technology, audio output changes, suspend and resume, and missing native dependencies. **Accept when** the installed build passes the matrix on each supported distribution and display configuration and provides a recovery message for missing dependencies.
- [ ] **BP-404 Package and document Linux support.** Publish installation prerequisites, tested configurations, verified codecs, and known platform capabilities. **Accept when** a fresh supported installation can play both reference fixtures and the published package matches the documented requirements.

### WebAssembly

- [ ] **BP-411 Prototype a browser media layer.** After BP-201 and the draft of BP-203, use a browser audio/video element and test how it composes with the Broiler canvas, menus, captions, and fullscreen. **Accept when** a deployed prototype presents playable audio and video and establishes how the retained-mode UI and browser media layer exchange focus and events.
- [ ] **BP-412 Replace whole-file browser ingestion.** Pass browser-owned files, handles, or object URLs for playback. Make folder browsing enumerate lightweight entries and read media or metadata only when needed. Release URLs and pending reads on replacement and close. **Accept when** opening a large file or browsing a folder avoids whole-file managed copies and repeated opens do not retain object URLs.
- [ ] **BP-413 Bridge browser events and accessible controls.** Map play rejection, readiness, buffering, seeking, end, errors, fullscreen, and tab visibility into shared state. Provide browser-accessible names and values for any canvas-based controls and handle user-gesture requirements. **Accept when** keyboard and screen-reader playback, blocked autoplay recovery, and returning from a background tab work on the supported browser matrix.
- [ ] **BP-414 Recover browser permissions and restore what can be reopened.** Store supported handles only with appropriate permissions. Keep persistent metadata and relink prompts for entries that need a new selection. **Accept when** revoked, expired, and unavailable permissions produce a clear reselection flow and browsing still works with the fallback picker.
- [ ] **BP-415 Verify the deployed browser app.** Test standalone and supported hosted subpaths with the published artifact, including launcher integration where applicable. Publish tested browser and codec combinations. **Accept when** real audio/video, seeking, fullscreen, and large-file behavior work from the deployed path on the supported browser matrix.

**Exit criteria.** A head ships playback support when its packaged or deployed app passes its own tests. The same queue and transport behavior works on every released head wherever the session advertises the required capabilities.

## M5. Complete local and network playback

### Tracks, captions, and video controls

- [ ] **BP-501 Discover and select audio, text, and chapter tracks.** Extend the session contract with stable track IDs, language, labels, default flags, and chapter boundaries where the backend provides them. **Accept when** a verified multitrack file can change audio and select a chapter without replacing the queue item.
- [ ] **BP-502 Add sidecar and embedded captions.** After BP-501 and the target head's video composition work, support SRT and WebVTT sidecars plus verified embedded formats. Add caption off/on, track selection, offset, readable styling, and language preferences. **Accept when** captions remain timed after seeks and rate changes and stay readable in fullscreen. Bound parsing and treat cue text as data.
- [ ] **BP-503 Add playback speed and navigation aids.** Expose supported rates and chapter jumps in the transport or Playback menu. Show whether pitch preservation is available. **Accept when** switching rates preserves position and captions, chapter navigation respects the current media's boundaries, and unavailable controls are clearly identified.
- [ ] **BP-504 Add video display options.** Provide supported aspect-fit, original sizing or fill, and rotation where appropriate. Add a media information view for codecs, dimensions, frame rate, duration, and track details that the backend actually reports. **Accept when** rotated and anamorphic fixtures display with expected proportions and settings can be reset.
- [ ] **BP-505 Add media keys and now-playing integration.** Register the active session with supported system or browser media controls. Publish supported actions and clear the session on close. **Accept when** play/pause and supported next/previous actions reach the same commands as the UI and controls stop targeting a closed session.
- [ ] **BP-506 Add compact playback where supported.** Implement a compact audio view or video picture-in-picture after fullscreen and composition are stable. **Accept when** switching presentation preserves the session and current time, keeps usable keyboard controls, and releases the secondary surface on exit.

### Long-form listening and audio continuity

- [ ] **BP-511 Add bookmarks and a sleep timer.** After BP-309 and BP-310, let users name and revisit positions and pause or stop at a chosen time or track end. **Accept when** bookmarks survive restart, the timer can be canceled, and a track-end timer stops before the queue advances.
- [ ] **BP-512 Verify gapless playback and optional loudness normalization.** Assess backend support and measure transitions using a continuous album fixture. Use existing loudness metadata where available and provide an off setting. **Accept when** supported gapless transitions meet a measured threshold and normalization remains consistent across skips and queue changes without clipping the reference fixtures.

### Network media

- [ ] **BP-521 Add direct HTTPS playback.** Build on BP-201 and BP-203. Accept explicit user-provided URLs, define allowed schemes and redirect behavior, enforce timeouts, honor browser CORS, and keep URL credentials and tokens out of diagnostics. **Accept when** supported direct sources play and blocked schemes, redirects, denied access, and timeouts produce useful errors.
- [ ] **BP-522 Add buffering, recovery, and nonseekable state.** Represent unknown duration, buffered data and seekable ranges when available, and bounded retries that users can cancel. **Accept when** interrupted playback can recover or stop with a clear next action and a nonseekable source cannot receive a seek command through any input path.
- [ ] **BP-523 Add HLS on supported heads.** After BP-521 and BP-522, deliver on-demand and live streams, plus a live-edge action when a seekable live window is available. Test variant changes, expired windows, and stalled segments. **Accept when** the user can distinguish live from on-demand playback and resume playback or return to the live edge after an interruption.
- [ ] **BP-524 Support explicit playback of remote playlist entries.** Connect imported URL entries to the direct source flow. Bound playlist nesting, redirects, and retries. **Accept when** loading or previewing a list does not contact remote entries, and explicit playback applies the same URL and capability checks as **Open URL**.

**Exit criteria.** Released heads pass their capability matrices with representative fixtures for subtitles, track selection, long-form sessions, and network sources where supported. Users can tell what controls are available, operate them with a keyboard, and recover from common playback failures.

## Work that applies to every release

- [ ] **BP-901 Split deterministic tests from platform output tests.** Cover shared contracts, queue transitions, and migration with a fake backend and clock. Keep routing-only fixtures distinct from decodable fixtures. **Accept when** deterministic tests run without native audio/video and packaged verification separately demonstrates real output. [Broadplay CI](../.github/workflows/ci.yml)
- [ ] **BP-902 Maintain a source and capability matrix.** Track each tested head, OS or browser, container/codec pairing, source type, and advanced control. Record fixture provenance and reproducible verification steps. **Accept when** support claims in downloads, help, and pickers trace to the relevant release evidence.
- [ ] **BP-903 Measure playback and scanning performance.** Record first output, seek response, UI responsiveness, memory, and resource counts across large files, long queues, folder scans, and repeated replacements. **Accept when** each released head has measured baselines, thresholds, and a repeatable regression check.
- [ ] **BP-904 Bound untrusted media and metadata processing.** Set supported limits for playlists, captions, artwork, tags, and cached data. Review the dependencies that parse each input and exercise malformed, oversized, and truncated samples. **Accept when** rejection is recoverable and released builds stay within the documented resource bounds.
- [ ] **BP-905 Add privacy-aware playback diagnostics.** Record backend, version, state transitions, and an actionable error. Provide a user-copyable report that excludes paths, URLs, and credentials by default. **Accept when** a report can distinguish an unsupported codec from a device or backend failure without exposing those values.
- [ ] **BP-906 Verify accessibility and localization on every shipped head.** Check focus restoration, keyboard-only use, screen-reader status announcements, non-color-only error indicators, touch targets, UI scaling, and long or bidirectional filenames. **Accept when** core playback, queue, and recovery flows pass the documented per-head checks.
- [ ] **BP-907 Keep packaging and upgrades verifiable.** Verify Windows and Linux NativeAOT packages and the WebAssembly artifact for the heads being released. Record backend dependency and license reviews and test state upgrades and recovery. **Accept when** a fresh install and upgrade can play the verified fixtures and existing queue data remains recoverable.

Broadplay owns the player interface, queue, collection, settings, and playback policy. Each platform head owns file access, playback integration, rendering surfaces, and OS controls. Put reusable codec, stream, and output capabilities in `Broiler.Media` when appropriate and record the required package version in the dependent issue.

## Later candidates to evaluate

- [ ] **BP-951 Explore timeline thumbnails, frame stepping, and A–B repeat.** Prototype on the verified video path. Advance the parts that provide responsive results without inflating memory or disrupting the main decoder.
- [ ] **BP-952 Explore crossfade and equalization.** Check whether the audio backend supports concurrent prebuffering and a consistent processing chain. Measure latency and transitions before committing to a shared UI.
- [ ] **BP-953 Explore an album and artist library.** Prototype grouping for multidisc albums, compilations, and series. Advance once BP-307 and BP-308 can provide the required metadata and library scale.
- [ ] **BP-954 Evaluate casting and remote libraries.** Identify the required discovery, pairing, permissions, and playback delegation on the target platforms. Advance once local and direct network playback are stable and support costs are understood.
- [ ] **BP-955 Evaluate additional sources.** Prioritize remote feeds, DASH, network shares, optical media, or additional codecs based on demand, licensing, and which released heads can support them. Add an explicit source and compatibility test plan before promising support.
