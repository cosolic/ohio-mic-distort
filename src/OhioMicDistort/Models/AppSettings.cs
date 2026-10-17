namespace OhioMicDistort.Models;

public sealed class AppSettings
{
    /// <summary>
    /// Selected microphone's EndpointGuid (e.g., "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}").
    /// </summary>
    public string? SelectedMicEndpointGuid { get; set; }

    /// <summary>
    /// Distortion intensity from 0% to 100%.
    /// Maps to Preamp gain (+0 dB to +60 dB) plus hard-clip EQ shaping.
    /// </summary>
    public double IntensityPercent { get; set; } = 65.0;

    /// <summary>
    /// Adds aggressive low-end punch and harsh mid-range boost so the signal
    /// hard-clips into classic blown-out "toaster / Xbox 360 mic" distortion.
    /// </summary>
    public bool EnableToasterClipEq { get; set; } = true;

    /// <summary>
    /// Win32 Virtual-Key code for the global toggle hotkey (default: 0x77 = F8).
    /// </summary>
    public int HotkeyVirtualKey { get; set; } = 0x77;

    /// <summary>
    /// Win32 modifier flags (MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4).
    /// </summary>
    public uint HotkeyModifiers { get; set; } = 0;

    /// <summary>
    /// Human-readable label for the bound hotkey.
    /// </summary>
    public string HotkeyDisplay { get; set; } = "F8";

    /// <summary>
    /// Path to Equalizer APO's config directory.
    /// </summary>
    public string EqualizerApoConfigDir { get; set; } = @"C:\Program Files\EqualizerAPO\config";
}
