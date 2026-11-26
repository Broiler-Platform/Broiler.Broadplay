// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Broadplay;

/// <summary>Linux entry point for Broiler Broadplay.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Composition root: without a codec catalog the renderer cannot decode
        // the images a document embeds or open graphics files.
        Broiler.Graphics.Imaging.BImageCodecs.Use(
            new Broiler.Media.MediaCodecCatalog(Broiler.Media.Image.Managed.ManagedImageCodecs.CreateCodecs()));

        // Font resolver for software rasterizer on Linux systems without DirectWrite.
        Broiler.Graphics.Text.BSystemFonts.InstallFontFileResolver();

        LinuxBroadplayOptions options = LinuxBroadplayOptions.Parse(args);
        if (options.ShowHelp)
        {
            LinuxBroadplayOptions.PrintHelp();
            return 0;
        }

        if (options.OpenWindow && !OperatingSystem.IsLinux())
        {
            Console.WriteLine("Windowed Broiler Broadplay uses the Linux X11/OpenGL backend; running offscreen on this OS.");
            options = options with { OpenWindow = false, EnableEvdevInput = false, RunUntilClose = false };
        }

        using CancellationTokenSource shutdown = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            return await LinuxBroadplayRunner
                .RunAsync(options, shutdown.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

}
