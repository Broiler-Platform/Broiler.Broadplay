// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   8
// Annotated:        8/8
// Exempt:           0
// Human-reviewed:   0/8
// IP risk:          Low
// Security risk:    High
// Criteria:         6/6
// Resource impact:  8/10 max
// Unverified:       8
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Imaging;
using Broiler.Native.Windows;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Broiler.Broadplay;

/// <summary>Windows entry point for Broiler Broadplay.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=A32934
// Broiler-Falsified-If: a file opened in this head reaches a decoder the composition root did not compose, such as a PDF CCITTFaxDecode or JBIG2Decode stream filter
// Broiler-Human:        PENDING
[SupportedOSPlatform("windows7.0")]
internal static class Program
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=492F78
    // Broiler-Falsified-If: a graphic opened in the window is decoded by a codec outside ManagedImageCodecs.CreateCodecs(), because BImageCodecs.Use ran after BroadplayWindow was created or not at all
    // Broiler-Human:        PENDING
    [STAThread]
    private static int Main(string[] args)
    {
        _ = WindowNative.SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2, best effort.

        // Composition root. A viewer without a codec catalog cannot decode the
        // pictures a document embeds, and could not open a graphics file at all -
        // which for this application is half of what it does.
        BImageCodecs.Use(
            new Broiler.Media.MediaCodecCatalog(Broiler.Media.Image.Managed.ManagedImageCodecs.CreateCodecs()));

        try
        {
            using var window = new BroadplayWindow(args);
            return window.Run();
        }
        catch (Exception ex)
        {
            WindowNative.MessageBox(IntPtr.Zero, ex.ToString(), "Broiler Broadplay", MbIconError | MbOk);
            return 1;
        }
    }


    private const uint MbOk = 0x00000000;
    // Broiler-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=4AE07E
    // Broiler-Human:        PENDING
    private const uint MbIconError = 0x00000010;
}
