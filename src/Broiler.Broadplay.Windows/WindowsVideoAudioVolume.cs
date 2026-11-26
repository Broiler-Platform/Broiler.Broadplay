// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Broiler.Native.Windows;
using Broiler.Native.Windows.Wasapi;

namespace Broiler.Broadplay;

/// <summary>Adjusts only this process's playback sessions and preserves the user's mixer settings.</summary>
internal sealed partial class WindowsVideoAudioVolume
{
    private readonly Dictionary<string, (float Volume, int Muted, float AppliedVolume, int AppliedMute)> _baseline = new();

    public bool Apply(float volume, bool muted)
    {
        bool applied = false;
        Visit((id, audio) =>
        {
            if (audio.GetMasterVolume(out float currentVolume) < 0 || audio.GetMute(out int currentMute) < 0)
                return;
            if (!_baseline.TryGetValue(id, out var previous))
                previous = (currentVolume, currentMute, currentVolume, currentMute);
            if (Math.Abs(previous.AppliedVolume - currentVolume) > 0.0001f)
                previous.Volume = currentVolume;
            if (previous.AppliedMute != currentMute)
                previous.Muted = currentMute;

            float effectiveVolume = previous.Volume * volume;
            int effectiveMute = muted ? 1 : previous.Muted;
            if (audio.SetMasterVolume(effectiveVolume, 0) >= 0)
                previous.AppliedVolume = effectiveVolume;
            if (audio.SetMute(effectiveMute, 0) >= 0)
                previous.AppliedMute = effectiveMute;
            _baseline[id] = previous;
            applied = true;
        });
        return applied;
    }

    public void Restore()
    {
        if (_baseline.Count == 0)
            return;
        Visit((id, audio) =>
        {
            if (!_baseline.TryGetValue(id, out var previous))
                return;
            if (audio.GetMasterVolume(out float currentVolume) >= 0 &&
                Math.Abs(previous.AppliedVolume - currentVolume) < 0.0001f)
                _ = audio.SetMasterVolume(previous.Volume, 0);
            if (audio.GetMute(out int currentMute) >= 0 && currentMute == previous.AppliedMute)
                _ = audio.SetMute(previous.Muted, 0);
        });
        _baseline.Clear();
    }

    private static void Visit(Action<string, SimpleAudioVolume> visit)
    {
        int initialized = ComNative.CoInitializeEx(0, ComNative.COINIT_MULTITHREADED);
        if (initialized < 0 && initialized != ComNative.RPC_E_CHANGED_MODE)
            return;
        IMMDeviceEnumerator? devices = null;
        IMMDeviceCollection? outputs = null;
        try
        {
            Guid clsid = WindowsWasapiNative.MMDeviceEnumeratorClassId;
            Guid iid = WindowsWasapiNative.IMMDeviceEnumeratorId;
            if (ComNative.CoCreateInstance(in clsid, 0, ComNative.CLSCTX_INPROC_SERVER, in iid, out nint ptr) < 0)
                return;
            devices = Wrap<IMMDeviceEnumerator>(ptr);
            if (devices is null || devices.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out outputs) < 0 ||
                outputs.GetCount(out uint count) < 0)
                return;
            for (uint index = 0; index < count; index++)
            {
                if (outputs.Item(index, out IMMDevice device) < 0)
                    continue;
                try { VisitDevice(device, visit); }
                finally { ComNative.ReleaseComObject(device); }
            }
        }
        finally
        {
            ComNative.ReleaseComObject(outputs);
            ComNative.ReleaseComObject(devices);
            if (initialized >= 0)
                ComNative.CoUninitialize();
        }
    }

    private static void VisitDevice(IMMDevice device, Action<string, SimpleAudioVolume> visit)
    {
        Guid iid = typeof(SessionManager).GUID;
        if (device.Activate(ref iid, ComNative.CLSCTX_INPROC_SERVER, 0, out nint ptr) < 0)
            return;
        SessionManager? manager = Wrap<SessionManager>(ptr);
        SessionEnumerator? sessions = null;
        try
        {
            if (manager is null || manager.GetSessionEnumerator(out sessions) < 0 || sessions.GetCount(out int count) < 0)
                return;
            for (int index = 0; index < count; index++)
            {
                if (sessions.GetSession(index, out nint control) < 0)
                    continue;
                SessionControl? session = Wrap<SessionControl>(control);
                try
                {
                    if (session is not SimpleAudioVolume audio || session.GetProcessId(out uint pid) != 0 ||
                        pid != Environment.ProcessId || session.GetSessionInstanceIdentifier(out nint identifier) < 0)
                        continue;
                    try
                    {
                        if (Marshal.PtrToStringUni(identifier) is { } id)
                            visit(id, audio);
                    }
                    finally { Marshal.FreeCoTaskMem(identifier); }
                }
                finally { ComNative.ReleaseComObject(session); }
            }
        }
        finally
        {
            ComNative.ReleaseComObject(sessions);
            ComNative.ReleaseComObject(manager);
        }
    }

    private static T? Wrap<T>(nint pointer) where T : class
    {
        try { return ComNative.GetOrCreateComObject<T>(pointer); }
        finally { ComNative.ReleaseIUnknown(pointer); }
    }

    // Slot order follows the Windows SDK's audiopolicy.h and audioclient.h.
    [GeneratedComInterface, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    internal partial interface SessionManager
    {
        [PreserveSig] int GetAudioSessionControl(nint id, uint flags, out nint control);
        [PreserveSig] int GetSimpleAudioVolume(nint id, uint flags, out nint volume);
        [PreserveSig] int GetSessionEnumerator(out SessionEnumerator sessions);
    }

    [GeneratedComInterface, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    internal partial interface SessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out nint session);
    }

    [GeneratedComInterface, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    internal partial interface SessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out nint name);
        [PreserveSig] int SetDisplayName(nint name, nint context);
        [PreserveSig] int GetIconPath(out nint path);
        [PreserveSig] int SetIconPath(nint path, nint context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(in Guid grouping, nint context);
        [PreserveSig] int RegisterAudioSessionNotification(nint events);
        [PreserveSig] int UnregisterAudioSessionNotification(nint events);
        [PreserveSig] int GetSessionIdentifier(out nint id);
        [PreserveSig] int GetSessionInstanceIdentifier(out nint id);
        [PreserveSig] int GetProcessId(out uint process);
    }

    [GeneratedComInterface, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    internal partial interface SimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float volume, nint context);
        [PreserveSig] int GetMasterVolume(out float volume);
        [PreserveSig] int SetMute(int muted, nint context);
        [PreserveSig] int GetMute(out int muted);
    }
}
