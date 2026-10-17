namespace OhioMicDistort.Models;

/// <summary>
/// Represents a Windows CoreAudio Capture (Microphone) endpoint.
/// Playback/Headphone devices are never included.
/// </summary>
public sealed class MicDeviceInfo
{
    /// <summary>
    /// Full Windows MMDevice ID, e.g. "{0.0.1.00000000}.{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}"
    /// </summary>
    public required string FullId { get; init; }

    /// <summary>
    /// The endpoint GUID "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}" used by Equalizer APO's
    /// "Device:" command to lock DSP filters strictly to this microphone capture device.
    /// </summary>
    public required string EndpointGuid { get; init; }

    /// <summary>
    /// Full friendly name from Windows Audio (e.g., "Microphone (HyperX QuadCast)").
    /// </summary>
    public required string FriendlyName { get; init; }

    /// <summary>
    /// True if this microphone is currently the Windows default capture device.
    /// </summary>
    public bool IsDefault { get; init; }

    public string DisplayName => IsDefault ? $"{FriendlyName} (Default)" : FriendlyName;

    public override string ToString() => DisplayName;
}
