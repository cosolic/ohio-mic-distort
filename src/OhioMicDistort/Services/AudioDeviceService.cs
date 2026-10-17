using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OhioMicDistort.Models;

namespace OhioMicDistort.Services;

/// <summary>
/// Result of checking whether Equalizer APO is actually registered on a capture endpoint.
/// </summary>
public enum ApoDeviceStatus
{
    /// <summary>Equalizer APO's CLSID is present in the device's FxProperties — Windows will run the APO.</summary>
    Enabled,

    /// <summary>The device exists but Equalizer APO is NOT enabled on it — no rules will ever apply.</summary>
    NotInstalled,

    /// <summary>Could not be determined (registry key missing, access denied, non-Windows host).</summary>
    Unknown
}

/// <summary>
/// Discovers Windows Capture (Microphone) devices using NAudio CoreAudio API
/// and provides a read-only input level meter (never outputs to headphones).
/// </summary>
public sealed class AudioDeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private WasapiCapture? _activeCapture;

    /// <summary>
    /// Raised when new microphone audio samples arrive (0.0 to 1.0 peak level).
    /// This only measures input level for the UI meter; it never plays audio to headphones.
    /// </summary>
    public event Action<double>? InputLevelChanged;

    /// <summary>
    /// Lists all active Microphone (Capture) endpoints on Windows 10/11.
    /// Playback/Headphone (Render) endpoints are strictly excluded.
    /// </summary>
    public IReadOnlyList<MicDeviceInfo> GetCaptureDevices()
    {
        var result = new List<MicDeviceInfo>();

        string? defaultCaptureId = null;
        try
        {
            if (_enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications))
            {
                using var defaultDev = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                defaultCaptureId = defaultDev.ID;
            }
        }
        catch
        {
            // Ignore if no default capture device exists
        }

        var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        foreach (var device in devices)
        {
            try
            {
                string fullId = device.ID;
                string endpointGuid = ExtractEndpointGuid(fullId);

                result.Add(new MicDeviceInfo
                {
                    FullId = fullId,
                    EndpointGuid = endpointGuid,
                    FriendlyName = device.FriendlyName,
                    IsDefault = string.Equals(fullId, defaultCaptureId, StringComparison.OrdinalIgnoreCase)
                });
            }
            catch
            {
                // Skip inaccessible endpoint
            }
            finally
            {
                device.Dispose();
            }
        }

        return result;
    }

    /// <summary>
    /// Starts monitoring the selected microphone's input level for the visual meter.
    /// Does NOT play or route any audio to speakers/headphones.
    /// </summary>
    public void StartInputLevelMonitor(string? fullDeviceId)
    {
        StopInputLevelMonitor();

        if (string.IsNullOrWhiteSpace(fullDeviceId))
        {
            return;
        }

        try
        {
            MMDevice? device = _enumerator.GetDevice(fullDeviceId);
            if (device is null)
            {
                return;
            }

            // useEventSync: false — event-synchronous capture is not supported by every endpoint (and is
            // frequently unavailable once an APO chain such as Equalizer APO is inserted), which leaves the
            // meter dead. Polling capture works everywhere. 40 ms keeps the meter smooth without adding
            // noticeable latency or CPU churn.
            _activeCapture = new WasapiCapture(device, false, 40);
            _activeCapture.DataAvailable += OnCaptureDataAvailable;
            _activeCapture.StartRecording();
        }
        catch
        {
            StopInputLevelMonitor();
        }
    }

    public void StopInputLevelMonitor()
    {
        if (_activeCapture is not null)
        {
            try
            {
                _activeCapture.DataAvailable -= OnCaptureDataAvailable;
                _activeCapture.StopRecording();
            }
            catch
            {
                // Ignore stop errors
            }
            finally
            {
                _activeCapture.Dispose();
                _activeCapture = null;
            }
        }

        InputLevelChanged?.Invoke(0.0);
    }

    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_activeCapture is null || e.BytesRecorded == 0)
        {
            return;
        }

        var format = _activeCapture.WaveFormat;
        float maxPeak = 0f;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (int i = 0; i <= e.BytesRecorded - 4; i += 4)
            {
                float sample = Math.Abs(BitConverter.ToSingle(e.Buffer, i));
                if (sample > maxPeak)
                {
                    maxPeak = sample;
                }
            }
        }
        else if (format.BitsPerSample == 16)
        {
            for (int i = 0; i <= e.BytesRecorded - 2; i += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, i);
                float normalized = Math.Abs(sample / 32768f);
                if (normalized > maxPeak)
                {
                    maxPeak = normalized;
                }
            }
        }

        double clamped = Math.Clamp(maxPeak, 0.0, 1.0);
        InputLevelChanged?.Invoke(clamped);
    }

    /// <summary>
    /// CLSIDs Equalizer APO registers for its APO chain. Windows lists them in the value names of
    /// HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\{endpoint guid}\FxProperties
    /// only when the user has actually ticked that device in Equalizer APO's Configurator.
    /// </summary>
    private static readonly string[] EqualizerApoClsids =
    {
        "{EACD2258-FCAC-4FF4-B36D-419E924A6D79}",
        "{EC1CC9CE-FAED-4822-828A-82A81A6F018F}"
    };

    /// <summary>Registry path (relative to HKLM) Windows stores per-device audio FX/APO registrations under.</summary>
    public const string CaptureDevicesRegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture";

    /// <summary>
    /// Checks the Windows registry to see whether Equalizer APO is enabled on the given capture endpoint.
    /// Without this, rules are written happily to ohio-mic-distort.txt while the APO never loads, and the
    /// user has no idea why nothing sounds distorted.
    /// </summary>
    /// <param name="endpointGuid">
    /// Capture endpoint GUID, with or without braces, e.g. "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}".
    /// </param>
    public static ApoDeviceStatus GetEqualizerApoDeviceStatus(string? endpointGuid)
    {
        if (string.IsNullOrWhiteSpace(endpointGuid))
        {
            return ApoDeviceStatus.Unknown;
        }

        string guid = endpointGuid.Trim();
        if (!guid.StartsWith('{'))
        {
            guid = $"{{{guid}}}";
        }

        try
        {
            RegistryKey? captureKey = OpenCaptureDevicesKey();
            if (captureKey is null)
            {
                return ApoDeviceStatus.Unknown;
            }

            using (captureKey)
            {
                using RegistryKey? deviceKey = captureKey.OpenSubKey(guid);
                if (deviceKey is null)
                {
                    // Endpoint not present under Capture at all (hot-plugged / renamed) — can't tell.
                    return ApoDeviceStatus.Unknown;
                }

                using RegistryKey? fxKey = deviceKey.OpenSubKey("FxProperties");
                if (fxKey is null)
                {
                    // No FX registered on this endpoint whatsoever, so Equalizer APO is definitely not enabled.
                    return ApoDeviceStatus.NotInstalled;
                }

                // FxProperties value names look like "{clsid-upstream},{clsid-downstream}".
                foreach (string valueName in fxKey.GetValueNames())
                {
                    foreach (string clsid in EqualizerApoClsids)
                    {
                        if (valueName.Contains(clsid, StringComparison.OrdinalIgnoreCase))
                        {
                            return ApoDeviceStatus.Enabled;
                        }
                    }
                }

                return ApoDeviceStatus.NotInstalled;
            }
        }
        catch
        {
            // Registry access can be blocked by policy on locked-down machines — never crash over it.
            return ApoDeviceStatus.Unknown;
        }
    }

    /// <summary>
    /// Opens HKLM\...\MMDevices\Audio\Capture, preferring the 64-bit registry view where Windows
    /// stores audio endpoint FX registrations.
    /// </summary>
    private static RegistryKey? OpenCaptureDevicesKey()
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            RegistryKey? key = baseKey.OpenSubKey(CaptureDevicesRegistryPath);
            if (key is not null)
            {
                return key;
            }
        }
        catch
        {
            // Fall back to the default view below.
        }

        using RegistryKey defaultBaseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        return defaultBaseKey.OpenSubKey(CaptureDevicesRegistryPath);
    }

    /// <summary>
    /// Extracts "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}" from a Windows MMDevice ID

    /// like "{0.0.1.00000000}.{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}".
    /// Equalizer APO matches this GUID in its "Device:" selector.
    /// </summary>
    public static string ExtractEndpointGuid(string mmDeviceId)
    {
        int separatorIndex = mmDeviceId.IndexOf("}.{", StringComparison.Ordinal);
        if (separatorIndex >= 0 && separatorIndex + 2 < mmDeviceId.Length)
        {
            return mmDeviceId[(separatorIndex + 2)..];
        }

        return mmDeviceId;
    }

    public void Dispose()
    {
        StopInputLevelMonitor();
        _enumerator.Dispose();
    }
}
