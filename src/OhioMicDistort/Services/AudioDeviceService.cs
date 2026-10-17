using NAudio.CoreAudioApi;
using NAudio.Wave;
using OhioMicDistort.Models;

namespace OhioMicDistort.Services;

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

            _activeCapture = new WasapiCapture(device, true, 30);
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
