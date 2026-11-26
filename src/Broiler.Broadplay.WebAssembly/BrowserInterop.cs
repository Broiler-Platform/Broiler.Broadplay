// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices.JavaScript;

namespace Broiler.Broadplay.WebAssembly;

/// <summary>
/// Managed-to-page bridge for the browser Broadplay application.
/// </summary>
internal static partial class BrowserInterop
{
    [JSImport("broadplay.scheduleFrame", "main.js")]
    internal static partial void ScheduleFrame();

    [JSImport("broadplay.setTitle", "main.js")]
    internal static partial void SetTitle(string title);

    [JSImport("broadplay.setCursor", "main.js")]
    internal static partial void SetCursor(string cursor);

    [JSImport("broadplay.openUrl", "main.js")]
    internal static partial void OpenUrl(string url);

    [JSImport("broadplay.webMenu", "main.js")]
    internal static partial void WebMenu(string action);

    [JSImport("broadplay.ready", "main.js")]
    internal static partial void Ready(double logicalWidth, double logicalHeight);

    [JSImport("broadplay.failed", "main.js")]
    internal static partial void Failed(string exceptionType, string details);

    [JSImport("broadplay.publishFrame", "main.js")]
    internal static partial void PublishFrame(
        double frameIndex,
        bool caretActive,
        double caretX,
        double caretY,
        double caretWidth,
        double caretHeight,
        int caretIndex,
        int selectionStart,
        int selectionLength,
        string statusText,
        bool darkTheme);

    [JSImport("broadplay.prefersDarkScheme", "main.js")]
    internal static partial bool PrefersDarkScheme();

    [JSImport("broadplay.prefersReducedMotion", "main.js")]
    internal static partial bool PrefersReducedMotion();

    [JSImport("broadplay.requestOpenFile", "main.js")]
    internal static partial void RequestOpenFile(string acceptExtensions);

    [JSImport("broadplay.readCollection", "main.js")]
    internal static partial string ReadCollection();

    [JSImport("broadplay.writeCollection", "main.js")]
    internal static partial void WriteCollection(string state);

    [JSImport("broadplay.requestOpenFolder", "main.js")]
    internal static partial void RequestOpenFolder(string acceptExtensions);

    [JSImport("broadplay.reportOperation", "main.js")]
    internal static partial void ReportOperation(string outcome, string message);
}
