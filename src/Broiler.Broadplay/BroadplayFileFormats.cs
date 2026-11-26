// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.UI.FileDialog;

namespace Broiler.Broadplay;

internal sealed class BroadplayFileFormats
{
    public static BroadplayFileFormats CreateDefault() => new();

    public UiFileDialogFilter[] CreateOpenFilters()
    {
        string audio = BroadplayMediaFormats.AllAudioFilterPattern;
        string video = BroadplayMediaFormats.AllVideoFilterPattern;
        return
        [
            new("Media", audio + ";" + video, ".wav"),
            new("Audio", audio, ".wav"),
            new("Video", video, ".mp4"),
            new("All files", "*.*", ""),
        ];
    }

    public string CreateAcceptExtensions() =>
        (BroadplayMediaFormats.AllAudioFilterPattern + ";" + BroadplayMediaFormats.AllVideoFilterPattern)
            .Replace("*", string.Empty).Replace(';', ',');
}
