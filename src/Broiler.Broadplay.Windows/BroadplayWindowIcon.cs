// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using Broiler.Native.Windows;
using static Broiler.Native.Windows.WindowNative;

namespace Broiler.Broadplay;

/// <summary>
/// Puts the icon the build embeds in the executable (<c>ApplicationIcon</c>) on the main window,
/// and reloads it at the right size when the window moves to a monitor with another DPI.
/// </summary>
internal sealed class BroadplayWindowIcon : IDisposable
{
    private const int ApplicationIconId = 32512;

    private const uint WmSetIcon = 0x0080;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmDpiChanged = 0x02E0;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const uint ImageIcon = 1;
    private const int SmCxIcon = 11;
    private const int SmCyIcon = 12;
    private const int SmCxSmIcon = 49;
    private const int SmCySmIcon = 50;

    private readonly SubclassProc _callback;
    private IntPtr _window;
    private IntPtr _big;
    private IntPtr _small;

    private BroadplayWindowIcon(IntPtr window)
    {
        _window = window;
        _callback = HandleMessage;
    }

    public static BroadplayWindowIcon? TryAttach(IntPtr window)
    {
        var icon = new BroadplayWindowIcon(window);
        if (!icon.Load(DpiOf(window)))
            return null;

        _ = SetWindowSubclass(window, icon._callback, 1, 0);
        return icon;
    }

    public void Dispose()
    {
        if (_window != IntPtr.Zero)
        {
            RemoveWindowSubclass(_window, _callback, 1);
            SendMessage(_window, WmSetIcon, new IntPtr(IconBig), IntPtr.Zero);
            SendMessage(_window, WmSetIcon, new IntPtr(IconSmall), IntPtr.Zero);
            _window = IntPtr.Zero;
        }

        DestroyIcons(_big, _small);
        _big = IntPtr.Zero;
        _small = IntPtr.Zero;
    }

    private bool Load(uint dpi)
    {
        IntPtr module = GetModuleHandle(null);
        IntPtr big = LoadImage(module, new IntPtr(ApplicationIconId), ImageIcon,
            MetricForDpi(SmCxIcon, dpi), MetricForDpi(SmCyIcon, dpi), 0);
        IntPtr small = LoadImage(module, new IntPtr(ApplicationIconId), ImageIcon,
            MetricForDpi(SmCxSmIcon, dpi), MetricForDpi(SmCySmIcon, dpi), 0);

        if (big == IntPtr.Zero || small == IntPtr.Zero)
        {
            DestroyIcons(big, small);
            return false;
        }

        SendMessage(_window, WmSetIcon, new IntPtr(IconBig), big);
        SendMessage(_window, WmSetIcon, new IntPtr(IconSmall), small);

        DestroyIcons(_big, _small);
        _big = big;
        _small = small;
        return true;
    }

    private IntPtr HandleMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        nuint subclassId, nuint referenceData)
    {
        if (message == WmDpiChanged)
        {
            _ = Load(unchecked((uint)((long)wParam & 0xFFFF)));
        }
        else if (message == WmNcDestroy)
        {
            RemoveWindowSubclass(window, _callback, 1);
            _window = IntPtr.Zero;
            Dispose();
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static void DestroyIcons(IntPtr big, IntPtr small)
    {
        if (big != IntPtr.Zero)
            DestroyIcon(big);
        if (small != IntPtr.Zero)
            DestroyIcon(small);
    }

    private static uint DpiOf(IntPtr window)
    {
        uint dpi = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393) ? GetDpiForWindow(window) : 0;
        return dpi == 0 ? 96u : dpi;
    }

    private static int MetricForDpi(int index, uint dpi) =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)
            ? GetSystemMetricsForDpi(index, dpi)
            : GetSystemMetrics(index);

}
