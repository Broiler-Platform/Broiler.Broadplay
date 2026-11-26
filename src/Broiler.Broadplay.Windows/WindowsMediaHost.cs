// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Native.Windows;
using Broiler.Native.Windows.MediaFoundation;
using Broiler.Native.Windows.MediaFoundation.Capture;

namespace Broiler.Broadplay;

/// <summary>
/// Windows Media Foundation playback and stream probing.
/// </summary>
[SupportedOSPlatform("windows7.0")]
internal sealed partial class WindowsMediaHost : IBroadplayAsyncMediaHost
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipSiblings = 0x04000000;

    private readonly Func<IntPtr> _getParentHwnd;
    private readonly Func<double> _getDpiScale;
    private readonly Func<Action, bool>? _postToUi;
    private readonly object _videoGate = new();
    private readonly List<WindowsVideoSession> _videos = [];
    private bool _disposed;

    public WindowsMediaHost(Func<IntPtr> getParentHwnd, Func<double> getDpiScale, Func<Action, bool>? postToUi = null)
    {
        _getParentHwnd = getParentHwnd ?? throw new ArgumentNullException(nameof(getParentHwnd));
        _getDpiScale = getDpiScale ?? throw new ArgumentNullException(nameof(getDpiScale));
        _postToUi = postToUi;
    }

    public bool SupportsAudio => true;

    public bool SupportsVideo => true;

    public bool TryOpen(string path, BroadplayMediaKind kind, out IBroadplayMediaSession? session, out string? error) =>
        Unpack(OpenAsync(new BroadplayMediaSource.LocalFile(path, kind), CancellationToken.None)
            .GetAwaiter().GetResult(), out session, out error);

    public bool TryOpenBytes(string name, ReadOnlyMemory<byte> bytes, BroadplayMediaKind kind,
        out IBroadplayMediaSession? session, out string? error) =>
        Unpack(OpenAsync(new BroadplayMediaSource.Bytes(name, bytes, kind), CancellationToken.None)
            .GetAwaiter().GetResult(), out session, out error);

    private static bool Unpack(BroadplayOpenResult result, out IBroadplayMediaSession? session, out string? error)
    {
        session = result.Session;
        error = result.Error;
        return session is not null;
    }

    public async Task<BroadplayOpenResult> OpenAsync(BroadplayMediaSource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int ownerThread = Environment.CurrentManagedThreadId;
        nint child = 0;
        string extension = Path.GetExtension(source.Name);
        nint parent = _getParentHwnd();
        if (parent != 0)
        {
            child = WindowNative.CreateWindowEx(0, "STATIC", string.Empty, WsChild | WsVisible | WsClipSiblings,
                -320, -180, 320, 180, parent, 0, 0, 0);
        }

        string? temporaryPath = null;
        try
        {
            string path = source.Name;
            BroadplayMediaKind kindHint = source.KindHint;
            if (source is BroadplayMediaSource.Bytes bytes)
            {
                if (bytes.Content.IsEmpty)
                    return new(null, "The file is empty.");
                if (extension.Length == 0)
                    extension = bytes.KindHint == BroadplayMediaKind.Audio ? ".wav" : ".mp4";
                temporaryPath = Path.Combine(Path.GetTempPath(), "broiler-broadplay-" + Guid.NewGuid().ToString("N") + extension);
                await using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.Asynchronous);
                await output.WriteAsync(bytes.Content, cancellationToken).ConfigureAwait(false);
                path = temporaryPath;
            }
            else
            {
                byte[] signature = await Task.Run(() => ReadSignature(path), cancellationToken).ConfigureAwait(false);
                if (signature.Length == 0)
                    return new(null, "The file is empty.");
                if (!BroadplayApp.TryIdentify(path, signature, out kindHint))
                    return new(null, "Choose an audio or video file. Use Plate for documents and images.");
            }

            BroadplayMediaKind kind = await Task.Run(() => ProbeKind(path), cancellationToken).ConfigureAwait(false)
                ?? kindHint;
            cancellationToken.ThrowIfCancellationRequested();
            BroadplayOpenResult result;
            if (kind == BroadplayMediaKind.Video)
            {
                if (child == 0)
                    return new(null, "The video surface could not be created. Reopen the player and try again.");
                nint surface = child;
                string? temp = temporaryPath;
                result = await WindowsVideoSession.OpenAsync(path, surface, _getDpiScale, () =>
                {
                    DestroySurface(surface, ownerThread);
                    TryDeleteTempFile(temp);
                }, cancellationToken).ConfigureAwait(false);
                if (result.Session is not null)
                {
                    child = 0;
                    temporaryPath = null;
                    var video = (WindowsVideoSession)result.Session;
                    lock (_videoGate)
                    {
                        if (_disposed)
                        {
                            video.Dispose();
                            return new(null, "The player is closing.");
                        }
                        _videos.RemoveAll(opened => opened.Released.IsCompleted);
                        _videos.Add(video);
                    }
                }
                return result;
            }

            string audioPath = path;
            string? ownedPath = temporaryPath;
            result = await Task.Run(() => OpenAudio(audioPath, ownedPath, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (result.Session is not null)
                temporaryPath = null;
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(null, error.Message);
        }
        finally
        {
            DestroySurface(child, ownerThread);
            TryDeleteTempFile(temporaryPath);
        }
    }

    private static byte[] ReadSignature(string path)
    {
        using FileStream input = File.OpenRead(path);
        var bytes = new byte[32];
        int length = input.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        return length == bytes.Length ? bytes : bytes[..length];
    }

    private static BroadplayMediaKind? ProbeKind(string path)
    {
        const int firstAudioStream = unchecked((int)0xFFFFFFFD);
        int initialized = ComNative.CoInitializeEx(0, ComNative.COINIT_MULTITHREADED);
        if (initialized < 0 && initialized != ComNative.RPC_E_CHANGED_MODE)
            return null;

        bool started = false;
        IMFSourceReader? reader = null;
        try
        {
            started = MediaFoundationPlatformNative.MFStartup(MediaFoundationPlatformNative.MF_VERSION,
                MediaFoundationPlatformNative.MFSTARTUP_NOSOCKET) >= 0;
            if (!started || MFCreateSourceReaderFromURL(path, 0, out reader) < 0)
                return null;
            if (HasStream(WindowsMediaFoundationNative.MF_SOURCE_READER_FIRST_VIDEO_STREAM))
                return BroadplayMediaKind.Video;
            return HasStream(firstAudioStream) ? BroadplayMediaKind.Audio : null;
        }
        finally
        {
            ComNative.ReleaseComObject(reader);
            if (started)
                _ = MediaFoundationPlatformNative.MFShutdown();
            if (initialized >= 0)
                ComNative.CoUninitialize();
        }

        bool HasStream(int index)
        {
            int result = reader!.GetNativeMediaType(index, 0, out var type);
            ComNative.ReleaseComObject(type);
            return result >= 0;
        }
    }

    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MFCreateSourceReaderFromURL(string path, nint attributes, out IMFSourceReader reader);

    private static BroadplayOpenResult OpenAudio(string path, string? temporaryPath, CancellationToken cancellationToken)
    {
        BroadplayMediaInfo? analyzedInfo = null;
        BroadplayAudioAnalysisStatus analysisStatus = BroadplayAudioAnalysisStatus.UnsupportedFormat;
        string? analysisError = null;
        if (BroadplayMediaFormats.HasAudioExtension(path) || Path.GetExtension(path).Length == 0)
        {
            using FileStream input = File.OpenRead(path);
            analysisStatus = BroadplayAudioAnalyzer.Analyze(input, path, out analyzedInfo, out analysisError);
            if (analysisStatus == BroadplayAudioAnalysisStatus.InvalidData)
                return new(null, analysisError ?? "Could not decode this audio stream.");
        }

        if (WindowsAudioMediaSession.TryOpen(path, analyzedInfo, temporaryPath, out IBroadplayMediaSession? session,
            out string? error, cancellationToken))
        {
            return new(session, null);
        }

        if (analysisStatus == BroadplayAudioAnalysisStatus.Succeeded && analyzedInfo is not null &&
            WindowsManagedAudioSession.TryOpen(path, analyzedInfo, temporaryPath, out session,
                out string? managedError, cancellationToken))
        {
            return new(session, null);
        }

        return new(null, error);
    }

    private void DestroySurface(nint child, int ownerThread)
    {
        if (child == 0)
            return;
        if (Environment.CurrentManagedThreadId == ownerThread)
            _ = WindowNative.DestroyWindow(child);
        else
            _postToUi?.Invoke(() => WindowNative.DestroyWindow(child));
    }

    public void Dispose()
    {
        WindowsVideoSession[] open;
        lock (_videoGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            open = _videos.ToArray();
            _videos.Clear();
        }
        foreach (WindowsVideoSession video in open)
            video.Dispose();
        // Final shutdown is the only caller that waits for mixer restoration.
        var pending = Array.ConvertAll(open, video => video.Released);
        _ = Task.WaitAll(pending, TimeSpan.FromSeconds(2));
    }

    private static void TryDeleteTempFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try { File.Delete(path); }
        catch { }
    }
}
