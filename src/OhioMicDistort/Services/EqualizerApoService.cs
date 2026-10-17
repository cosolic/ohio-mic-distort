using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using OhioMicDistort.Models;

namespace OhioMicDistort.Services;

public sealed class EqualizerApoService
{
    public const string IncludeFileName = "ohio-mic-distort.txt";
    public const string MainConfigFileName = "config.txt";

    /// <summary>
    /// Checks whether Equalizer APO's config directory exists on disk.
    /// </summary>
    public bool IsEqualizerApoInstalled(string configDir)
    {
        return !string.IsNullOrWhiteSpace(configDir) && Directory.Exists(configDir);
    }

    /// <summary>
    /// Checks whether the current Windows user has write permission to the Equalizer APO config folder.
    /// </summary>
    public bool HasWriteAccess(string configDir)
    {
        if (!IsEqualizerApoInstalled(configDir))
        {
            return false;
        }

        string testFile = Path.Combine(configDir, ".ohio_write_test.tmp");
        try
        {
            File.WriteAllText(testFile, "ok");
            File.Delete(testFile);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Requests a one-time Administrator UAC prompt using icacls so standard user runs
    /// can write to C:\Program Files\EqualizerAPO\config without needing Admin every time.
    /// </summary>
    public bool GrantConfigFolderWriteAccess(string configDir)
    {
        if (!Directory.Exists(configDir))
        {
            return false;
        }

        try
        {
            // S-1-5-32-545 is the built-in Windows "Users" group SID (language-independent)
            var psi = new ProcessStartInfo
            {
                FileName = "icacls.exe",
                Arguments = $"\"{configDir}\" /grant *S-1-5-32-545:(OI)(CI)M /T",
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit(10000);
            return HasWriteAccess(configDir);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures C:\Program Files\EqualizerAPO\config\config.txt contains "Include: ohio-mic-distort.txt"
    /// and optionally comments out any unscoped global positive Preamp rules that would boost headphones.
    /// </summary>
    public void EnsureMainConfigInitialized(string configDir, bool disableUnscopedGlobalBoost = true)
    {
        Directory.CreateDirectory(configDir);
        string mainConfigPath = Path.Combine(configDir, MainConfigFileName);
        string includeDirective = $"Include: {IncludeFileName}";

        if (!File.Exists(mainConfigPath))
        {
            File.WriteAllText(mainConfigPath, includeDirective + Environment.NewLine, Encoding.UTF8);
            return;
        }

        string[] lines = File.ReadAllLines(mainConfigPath);
        var updatedLines = new List<string>(lines.Length + 2);
        bool hasDeviceScope = false;
        bool hasOurInclude = false;
        bool modified = false;

        foreach (string rawLine in lines)
        {
            string trimmed = rawLine.Trim();

            if (trimmed.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
            {
                string target = trimmed["Device:".Length..].Trim();
                hasDeviceScope = !string.Equals(target, "all", StringComparison.OrdinalIgnoreCase);
            }

            if (trimmed.Equals(includeDirective, StringComparison.OrdinalIgnoreCase))
            {
                hasOurInclude = true;
            }

            // If there is an unscoped positive Preamp in config.txt (outside any specific Device: block),
            // that is what causes Equalizer APO to boost the user's headphones as well as their mic!
            if (disableUnscopedGlobalBoost &&
                !hasDeviceScope &&
                trimmed.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParsePreampDb(trimmed, out double db) && db > 0.5)
                {
                    updatedLines.Add($"# [Disabled by OhioMicDistort to protect headphones] {rawLine}");
                    modified = true;
                    continue;
                }
            }

            updatedLines.Add(rawLine);
        }

        if (!hasOurInclude)
        {
            updatedLines.Add(includeDirective);
            modified = true;
        }

        if (modified)
        {
            File.WriteAllLines(mainConfigPath, updatedLines, Encoding.UTF8);
        }
    }

    /// <summary>
    /// Writes the active or bypassed Equalizer APO rule file (ohio-mic-distort.txt)
    /// locked strictly to the selected Microphone's Capture Endpoint GUID.
    /// Equalizer APO detects this file change in ~10ms and reloads DSP immediately.
    /// </summary>
    public string ApplyDistortionState(
        string configDir,
        MicDeviceInfo? selectedMic,
        bool isDistortionActive,
        double intensityPercent,
        bool enableToasterClipEq)
    {
        EnsureMainConfigInitialized(configDir);

        string includeFilePath = Path.Combine(configDir, IncludeFileName);
        string content = BuildIncludeFileContent(
            selectedMic,
            isDistortionActive,
            intensityPercent,
            enableToasterClipEq);

        File.WriteAllText(includeFilePath, content, Encoding.UTF8);
        return content;
    }

    /// <summary>
    /// Generates the exact Equalizer APO config text for previewing and writing to ohio-mic-distort.txt.
    /// </summary>
    public static string BuildIncludeFileContent(
        MicDeviceInfo? selectedMic,
        bool isDistortionActive,
        double intensityPercent,
        bool enableToasterClipEq)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# =====================================================================");
        sb.AppendLine("# OHIO MIC DISTORT - AUTO-GENERATED EQUALIZER APO CONFIG");
        sb.AppendLine("# Scoped strictly to your Microphone Capture Endpoint GUID.");
        sb.AppendLine("# Headphone / Playback devices are 100% isolated and untouched.");
        sb.AppendLine("# =====================================================================");

        if (!isDistortionActive || selectedMic is null)
        {
            sb.AppendLine("# Status: BYPASSED (Microphone is clean / normal volume)");
            return sb.ToString();
        }

        double clampedIntensity = Math.Clamp(intensityPercent, 0.0, 100.0);
        double normalized = clampedIntensity / 100.0;

        // Map 0-100% intensity to +0.0 dB .. +60.0 dB Preamp gain
        double preampDb = Math.Round(normalized * 60.0, 1);

        sb.AppendLine($"# Target Mic: {selectedMic.FriendlyName}");
        sb.AppendLine($"# Capture Endpoint GUID: {selectedMic.EndpointGuid}");
        sb.AppendLine($"Device: {selectedMic.EndpointGuid}");
        sb.AppendLine("Channel: all");
        sb.AppendLine( string.Create(CultureInfo.InvariantCulture, $"Preamp: {preampDb:F1} dB"));

        if (enableToasterClipEq && clampedIntensity > 0.0)
        {
            double bassGain = Math.Round(4.0 + (normalized * 16.0), 1);   // +4 dB to +20 dB low punch
            double midGain = Math.Round(6.0 + (normalized * 18.0), 1);    // +6 dB to +24 dB harsh vocal clip
            double highCut = Math.Round(-3.0 - (normalized * 9.0), 1);    // -3 dB to -12 dB toaster bandwidth roll-off

            sb.AppendLine("# Toaster / Hard-Clip Overdrive EQ Shaping");
            sb.AppendLine("Filter 1: ON HP Fc 90 Hz");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Filter 2: ON LSC Fc 155 Hz Gain {bassGain:F1} dB Q 0.70"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Filter 3: ON PK Fc 1750 Hz Gain {midGain:F1} dB Q 1.15"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Filter 4: ON HSC Fc 4300 Hz Gain {highCut:F1} dB Q 0.70"));
        }

        sb.AppendLine();
        sb.AppendLine("# Reset Device context so any subsequent Equalizer APO includes work normally");
        sb.AppendLine("Device: all");

        return sb.ToString();
    }

    /// <summary>
    /// Opens Equalizer APO's Device Selector / Configurator so the user can verify APO is installed on their mic.
    /// </summary>
    public bool LaunchEqualizerApoConfigurator(string configDir)
    {
        try
        {
            string? apoRoot = Directory.GetParent(configDir)?.FullName;
            if (string.IsNullOrWhiteSpace(apoRoot))
            {
                return false;
            }

            // Equalizer APO 1.3+ uses DeviceSelector.exe; older versions use Configurator.exe
            string deviceSelector = Path.Combine(apoRoot, "DeviceSelector.exe");
            string configurator = Path.Combine(apoRoot, "Configurator.exe");
            string exeToLaunch = File.Exists(deviceSelector) ? deviceSelector : configurator;

            if (!File.Exists(exeToLaunch))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exeToLaunch,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParsePreampDb(string line, out double db)
    {
        db = 0;
        string rest = line["Preamp:".Length..].Trim();
        if (rest.EndsWith("dB", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest[..^2].Trim();
        }

        return double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out db);
    }
}
