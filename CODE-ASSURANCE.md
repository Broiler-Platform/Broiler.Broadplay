# Broiler.Broadplay Code Assurance

GENERATED - DO NOT EDIT MANUALLY. Regenerate with
`dotnet run --project Broiler.Code/src/Broiler.Code.Review.Cli -c Release -- assurance generate --root Broiler.Broadplay`, which rewrites this file,
`HUMAN_REVIEW.md`, `assurance.manifest.json` and every generated source header from the
product tree.

**No code unit in this component carries a decision on its human line yet.** This report
records that absence precisely. It is not a claim that the code is reviewed, assured or safe,
and the figures below are the measurement of how far from that claim the per-unit record is.

## Summary

| Metric | Value |
|---|---:|
| Files scanned | 14 |
| Files not covered | 0 |
| Files carrying an annotation | 14 |
| Code units | 360 |
| Relevant | 253 |
| Exempt by predicate | 107 |
| Annotated | 253 of 253 (100%) |
| Human reviewed | 0 of 253 (0%) |
| Unverified | 253 |

## Review states

| State | Count |
|---|---:|
| NEW | 0 |
| AI_ASSESSED | 0 |
| HUMAN_PENDING | 253 |
| HUMAN_APPROVED_PENDING_FINGERPRINT | 0 |
| VERIFIED | 0 |
| STALE | 0 |
| EXEMPT | 107 |

## IP risk

| Value | Units |
|---|---:|
| None | 120 |
| Low | 133 |
| Medium | 0 |
| High | 0 |
| Unknown | 0 |
| *not annotated* | 0 |

## Security risk

| Value | Units |
|---|---:|
| None | 38 |
| Low | 93 |
| Medium | 66 |
| High | 50 |
| Critical | 6 |
| *not annotated* | 0 |

## Resource impact

| Metric | Value |
|---|---:|
| Maximum | 8 / 10 |
| Average over annotated units | 1.8 / 10 |
| Units scored | 253 |

## High-security review areas

- `Broiler.Broadplay.BroadplayHostWindow` in `src/Broiler.Broadplay.Windows/BroadplayHostWindow.cs` - Security=Critical, human line PENDING
- `Broiler.Broadplay.BroadplayHostWindow.Activate()` in `src/Broiler.Broadplay.Windows/BroadplayHostWindow.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayHostWindow.TryGetText(out string)` in `src/Broiler.Broadplay.Windows/BroadplayHostWindow.cs` - Security=Critical, human line PENDING
- `Broiler.Broadplay.BroadplayHostWindow.SetText(string)` in `src/Broiler.Broadplay.Windows/BroadplayHostWindow.cs` - Security=Critical, human line PENDING
- `Broiler.Broadplay.BroadplayHostWindow.CreateImage(ReadOnlySpan<byte>)` in `src/Broiler.Broadplay.Windows/BroadplayHostWindow.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayHostWindow.SetForegroundWindow(IntPtr)` in `src/Broiler.Broadplay.Windows/BroadplayHostWindow.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayPdfPrintPermission` in `src/Broiler.Broadplay.Windows/BroadplayPdfPrintPermission.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayPdfPrintPermission.For(DocumentReadResult)` in `src/Broiler.Broadplay.Windows/BroadplayPdfPrintPermission.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayWindow` in `src/Broiler.Broadplay.Windows/BroadplayWindow.cs` - Security=Critical, human line PENDING
- `Broiler.Broadplay.BroadplayWindow.OnCreated()` in `src/Broiler.Broadplay.Windows/BroadplayWindow.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayWindow.ReadClipboardText()` in `src/Broiler.Broadplay.Windows/BroadplayWindow.cs` - Security=Critical, human line PENDING
- `Broiler.Broadplay.BroadplayWindow.WriteClipboardText(string)` in `src/Broiler.Broadplay.Windows/BroadplayWindow.cs` - Security=Critical, human line PENDING
- `Broiler.Broadplay.BroadplayWindow.CloseNativeWindow()` in `src/Broiler.Broadplay.Windows/BroadplayWindow.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayWindow.CloseNativeWindowNow()` in `src/Broiler.Broadplay.Windows/BroadplayWindow.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayWindow.DestroyWindow(IntPtr)` in `src/Broiler.Broadplay.Windows/BroadplayWindow.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayWindow.SetFocus(IntPtr)` in `src/Broiler.Broadplay.Windows/BroadplayWindow.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayWindowsUiHost` in `src/Broiler.Broadplay.Windows/BroadplayWindowsUiHost.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayWindowsUiHost.ToScreenPlacement(BWindow?, UiHostWindowRequest)` in `src/Broiler.Broadplay.Windows/BroadplayWindowsUiHost.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayWindowsUiHost.NativePoint` in `src/Broiler.Broadplay.Windows/BroadplayWindowsUiHost.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayWindowsUiHost.ClientToScreen(IntPtr, ref NativePoint)` in `src/Broiler.Broadplay.Windows/BroadplayWindowsUiHost.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.Program` in `src/Broiler.Broadplay.Windows/Program.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.Program.Main()` in `src/Broiler.Broadplay.Windows/Program.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.Program.CreateFileFormats()` in `src/Broiler.Broadplay.Windows/Program.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.Program.CreatePdfServices()` in `src/Broiler.Broadplay.Windows/Program.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.Program.SetProcessDpiAwarenessContext(IntPtr)` in `src/Broiler.Broadplay.Windows/Program.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.Program.MessageBox(IntPtr, string, string, uint)` in `src/Broiler.Broadplay.Windows/Program.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayApp` in `src/Broiler.Broadplay/BroadplayApp.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayApp.SignatureLength` in `src/Broiler.Broadplay/BroadplayApp.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayApp.BroadplayApp(BroadplayUiHost, Action, BroadplayFileFormats?, IUiPrintHost?, Func<DocumentReadResult, BroadplayPrintPermission>?)` in `src/Broiler.Broadplay/BroadplayApp.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayApp.OpenFile(string)` in `src/Broiler.Broadplay/BroadplayApp.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayApp.ShowOpenDialog()` in `src/Broiler.Broadplay/BroadplayApp.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayApp.OpenDocument(string)` in `src/Broiler.Broadplay/BroadplayApp.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayApp.OpenImage(string)` in `src/Broiler.Broadplay/BroadplayApp.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayApp.ReadSignature(string, out bool)` in `src/Broiler.Broadplay/BroadplayApp.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayApp.GetDialogDirectory()` in `src/Broiler.Broadplay/BroadplayApp.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayDocumentFormat` in `src/Broiler.Broadplay/BroadplayFileFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayDocumentFormat.BroadplayDocumentFormat(DocumentCodec, string)` in `src/Broiler.Broadplay/BroadplayFileFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayFileFormats` in `src/Broiler.Broadplay/BroadplayFileFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayFileFormats.BroadplayFileFormats(IEnumerable<BroadplayDocumentFormat>)` in `src/Broiler.Broadplay/BroadplayFileFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayFileFormats.CreateDefault()` in `src/Broiler.Broadplay/BroadplayFileFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayFileFormats.With(params BroadplayDocumentFormat[])` in `src/Broiler.Broadplay/BroadplayFileFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayFileFormats.CreateOpenCatalog()` in `src/Broiler.Broadplay/BroadplayFileFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats.AllExtensions` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats.HasImageExtension(string)` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats.ContentTypeFor(string, ReadOnlySpan<byte>)` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats.MeasureDisplaySize(ReadOnlySpan<byte>, double)` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats.TryReadPixelSize(ReadOnlySpan<byte>, out int, out int)` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats.ContentTypeForSignature(ReadOnlySpan<byte>)` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats.TryReadPngSize(ReadOnlySpan<byte>, out int, out int)` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats.TryReadJpegSize(ReadOnlySpan<byte>, out int, out int)` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats.TryReadGifSize(ReadOnlySpan<byte>, out int, out int)` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats.TryReadBmpSize(ReadOnlySpan<byte>, out int, out int)` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayImageFormats.StartsWith(ReadOnlySpan<byte>, ReadOnlySpan<byte>)` in `src/Broiler.Broadplay/BroadplayImageFormats.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayUiHost` in `src/Broiler.Broadplay/BroadplayUiHost.cs` - Security=High, human line PENDING
- `Broiler.Broadplay.BroadplayUiHost.CreateImage(ReadOnlySpan<byte>)` in `src/Broiler.Broadplay/BroadplayUiHost.cs` - Security=High, human line PENDING

## Falsification criteria

| Metric | Value |
|---|---:|
| Units carrying a criterion | 176 |
| Units required to carry one | 56 |
| Required and missing | 0 |

A `Broiler-Falsified-If:` line states, at the declaration, the observation that would make
the unit wrong. `Security=High` says a unit is risky, which is a set and not a test; the
criterion is the test. It is required where `Security` is `High` or `Critical`, permitted
elsewhere, and `dotnet run --project Broiler.Code/src/Broiler.Code.Review.Cli -c Release -- assurance check --root Broiler.Broadplay` names every unit that owes one and carries none.

The line is a comment, so it is outside every fingerprint by construction: rewording a
criterion moves no recorded value here, in a file header or in
`assurance.manifest.json`, and invalidates nothing. That is the intended reading - a
criterion is an instruction to whoever reads the unit, not part of what a review is bound to.

## Exemption

Exemption is decided by one predicate in `CSharpAssuranceScanner`, not per unit, so
that the rule is reviewable in one place rather than in several hundred.

| Case | Units |
|---|---:|
| TrivialPropertyOrAccessor | 20 |
| ParameterAssigningConstructor | 0 |
| TrivialExpressionBodiedMember | 7 |
| CompilerSuppliedRecordOrEnumMember | 0 |
| DelegatingOverrideOrOperator | 0 |
| InsideAssemblyMarker | 0 |
| FieldDeclaringStorage | 71 |
| EnumMemberOfADeclaredVocabulary | 9 |
| DeclaredInSource | 0 |

## Per-unit exemptions

| Metric | Value |
|---|---:|
| Per-unit exemptions | 0 |

A per-unit `EXEMPT=<reason>` line exempts one unit by a reason a human wrote, for what the
predicate cannot see. Nothing mechanical checks that the reason is true, that it describes
the unit it sits on, or that it says anything at all, so every use is counted and named
here.

No unit in this component states a per-unit exemption.

## Files not covered

No file under a covered project's directory, and no file a covered project compiles in
through a `<Compile Include>` it states, is left out of the record.

## Change detection

`assurance.manifest.json` lists **every** code unit in the 2 covered assemblies -
360 of them, exempt and relevant alike - with the fingerprint of its declaration.
This manifest is a change-detection record, not a review. A unit listed there is watched, not reviewed:
the entry records what the declaration's tokens hashed to when the generator last ran, and
nothing else. What the manifest adds is that a unit the exemption predicate treats as
trivial is no longer invisible: a semantic change to one moves a value in a generated file
the check compares byte for byte. `dotnet run --project Broiler.Code/src/Broiler.Code.Review.Cli -c Release -- assurance check --root Broiler.Broadplay` holds the manifest to the tree.

Beside the units it lists **every covered file** - 14 of them - with a
fingerprint over the complete token stream of its compilation unit. A unit entry exists only
for a declaration kind the scanner enumerates, and an enumeration is a whitelist: an
`[assembly: ...]` attribute is a member of nothing and can be in no unit at all.
Nothing in a covered file can change without something moving here, whatever kind of declaration it is. Comments are outside the stream, because a token's
text is its own characters, so the generated header above and the annotation lines below move
no file fingerprint - which is what lets one generation be a fixed point.

## Verification

The generator and the check are one computation: `dotnet run --project Broiler.Code/src/Broiler.Code.Review.Cli -c Release -- assurance check --root Broiler.Broadplay` works out what the
generator would write and compares it with the tree byte for byte, so a record edited by
hand, or left behind by code that moved, is reported rather than trusted.

| Mode | Command | Effect |
|---|---|---|
| Generate | `dotnet run --project Broiler.Code/src/Broiler.Code.Review.Cli -c Release -- assurance generate --root Broiler.Broadplay` | Fills every `Fingerprint=TBF`, refreshes a decision the code has outrun into `STALE; Previous=...`, rewrites the generated headers, `HUMAN_REVIEW.md`, `assurance.manifest.json` and this file. |
| Check | `dotnet run --project Broiler.Code/src/Broiler.Code.Review.Cli -c Release -- assurance check --root Broiler.Broadplay` | Reports every generated artefact that is not byte-identical to what the generator would produce, every relevant unit with no annotation, every annotation this system cannot read, every fingerprint out of date and every unit at the top of the security vocabulary without a criterion. |
| Release | `dotnet run --project Broiler.Code/src/Broiler.Code.Review.Cli -c Release -- assurance check --root Broiler.Broadplay --release` | The check, and additionally every relevant unit left in a state that blocks a release. |

The fingerprint is six hex characters - 24 bits - of SHA-256 over the declaration's token
texts, joined by single spaces. Trivia is excluded because a token's text is its own
characters and never the comments or whitespace around it, so `dotnet format` moves no
fingerprint and an annotation is never part of what it describes. The value answers whether a
unit changed since it was reviewed. It is not a collision-free identifier across units and it
is not a cryptographic commitment.
