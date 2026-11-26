// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices.Marshalling;
using Broiler.Native.Windows;
using Broiler.Native.Windows.Wasapi;

namespace Broiler.Broadplay;

[GeneratedComClass]
internal sealed partial class WindowsAudioOutputMonitor(Action outputChanged) : IMMNotificationClient, IDisposable
{
    private IMMDeviceEnumerator? _enumerator;
    private bool _ownsCom;

    public static WindowsAudioOutputMonitor? TryStart(Action outputChanged)
    {
        var monitor = new WindowsAudioOutputMonitor(outputChanged);
        int initialized = ComNative.CoInitializeEx(0, ComNative.COINIT_MULTITHREADED);
        if (initialized < 0 && initialized != ComNative.RPC_E_CHANGED_MODE)
            return null;
        monitor._ownsCom = initialized >= 0;

        Guid clsid = WindowsWasapiNative.MMDeviceEnumeratorClassId;
        Guid iid = WindowsWasapiNative.IMMDeviceEnumeratorId;
        try
        {
            if (ComNative.CoCreateInstance(in clsid, 0, ComNative.CLSCTX_INPROC_SERVER, in iid, out nint pointer) >= 0)
            {
                try
                {
                    monitor._enumerator = ComNative.GetOrCreateComObject<IMMDeviceEnumerator>(pointer);
                    if (monitor._enumerator?.RegisterEndpointNotificationCallback(monitor) >= 0)
                        return monitor;
                }
                finally
                {
                    ComNative.ReleaseIUnknown(pointer);
                }
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }

        monitor.Dispose();
        return null;
    }

    public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
    {
        if (flow == EDataFlow.Render && role is ERole.Console or ERole.Multimedia)
            outputChanged();
        return 0;
    }

    public int OnDeviceStateChanged(string deviceId, DeviceState newState) => 0;
    public int OnDeviceAdded(string deviceId) => 0;
    public int OnDeviceRemoved(string deviceId) => 0;
    public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;

    public void Dispose()
    {
        if (_enumerator is { } enumerator)
        {
            _enumerator = null;
            _ = enumerator.UnregisterEndpointNotificationCallback(this);
            ComNative.ReleaseComObject(enumerator);
        }
        if (_ownsCom)
        {
            _ownsCom = false;
            ComNative.CoUninitialize();
        }
    }
}
