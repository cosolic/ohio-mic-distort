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
    /// Equalizer APO's config parser is C++ and reads files byte-for-byte: a UTF-8 BOM (EF BB BF)
    /// becomes part of the very first token, so line 1 no longer parses as "Device:" or "Include:"
    /// and the whole rule set is silently ignored. Every file we write must therefore be
    /// UTF-8 WITHOUT a BOM, and any BOM found while reading must be stripped.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Zero-width no-break space / BOM codepoint that must never reach Equalizer APO.</summary>
    private const char BomChar = '\uFEFF';

    /// <summary>Minimum Preamp gain applied at 0% intensity (already enough to clip a hot mic).</summary>
    public const double MinOverdrivePreampDb = 15.0;

    /// <summary>Maximum Preamp gain applied at 100% intensity (nuclear hard clipping).</summary>
    public const double MaxOverdrivePreampDb = 90.0;

    /// <summary>
    /// Maps the 0-100% intensity slider onto Overdrive Preamp gain (+15 dB .. +90 dB).
    /// Shared by the generated Equalizer APO rules and the UI label so both always agree.
    /// </summary>
    public static double GetPreampDb(double intensityPercent)
    {
        double normalized = Math.Clamp(intensityPercent, 0.0, 100.0) / 100.0;
        return Math.Round(MinOverdrivePreampDb + (normalized * (MaxOverdrivePreampDb - MinOverdrivePreampDb)), 1);
    }

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
    /// Ensures C:\Program Files\EqualizerAPO\config\config.txt ends with a clean
    /// "Device: all" context immediately followed by "Include: ohio-mic-distort.txt",
    /// and optionally comments out any unscoped global positive Preamp rules that would boost headphones.
    /// The file is always written as UTF-8 without a BOM.
    /// </summary>
    public void EnsureMainConfigInitialized(string configDir, bool disableUnscopedGlobalBoost = true)
    {
        Directory.CreateDirectory(configDir);
        string mainConfigPath = Path.Combine(configDir, MainConfigFileName);
        string includeDirective = $"Include: {IncludeFileName}";

        if (!File.Exists(mainConfigPath))
        {
            string freshConfig = "Device: all" + Environment.NewLine + includeDirective + Environment.NewLine;
            File.WriteAllText(mainConfigPath, freshConfig, Utf8NoBom);
            return;
        }

        string[] originalLines = ReadConfigLines(mainConfigPath);
        var updatedLines = new List<string>(originalLines.Length + 2);
        bool hasDeviceScope = false;

        foreach (string rawLine in originalLines)
        {
            string trimmed = rawLine.Trim();

            if (trimmed.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
            {
                string target = trimmed["Device:".Length..].Trim();
                hasDeviceScope = !string.Equals(target, "all", StringComparison.OrdinalIgnoreCase);
            }

            // Our include is re-appended below in one canonical form, so drop any stale copy of it
            // (plus a "Device: all" line that used to sit directly above it) to avoid duplicates.
            if (trimmed.Equals(includeDirective, StringComparison.OrdinalIgnoreCase))
            {
                if (updatedLines.Count > 0 &&
                    updatedLines[^1].Trim().Equals("Device: all", StringComparison.OrdinalIgnoreCase))
                {
                    updatedLines.RemoveAt(updatedLines.Count - 1);
                }

                continue;
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
                    continue;
                }
            }

            updatedLines.Add(rawLine);
        }

        // Equalizer APO treats "Device:" as a sticky filter that applies to every line below it.
        // If the user's own rules left the scope pinned to some other endpoint (e.g. their headphones),
        // an include written at the bottom would either be skipped for the microphone entirely or,
        // worse, applied to the wrong device. Resetting to "Device: all" on the line immediately above
        // the include guarantees the include is always evaluated, with the mic-specific scoping handled
        // inside ohio-mic-distort.txt itself.
        updatedLines.Add("Device: all");
        updatedLines.Add(includeDirective);

        if (!updatedLines.SequenceEqual(originalLines))
        {
            File.WriteAllLines(mainConfigPath, updatedLines, Utf8NoBom);
        }
    }

    /// <summary>
    /// Reads a config file, strips any UTF-8 BOM / stray U+FEFF characters, and splits into lines.
    /// </summary>
    private static string[] ReadConfigLines(string path)
    {
        string text = File.ReadAllText(path);

        if (text.Length > 0 && text[0] == BomChar)
        {
            text = text[1..];
        }

        text = text.Replace("\uFEFF", string.Empty);

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // A single trailing newline produces one empty element; drop it so blank lines don't accumulate
        // across repeated rewrites of config.txt.
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        return lines;
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

        File.WriteAllText(includeFilePath, content, Utf8NoBom);
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

        // Windows hands WASAPI capture buffers in 32-bit float, where values above 1.0 are not
        // clamped but kept (and then hard-clipped on the way out / by the consumer). Modest gain
        // amounts barely move the needle, so the range starts at +15 dB and ramps to +90 dB to
        // guarantee the signal slams into the float ceiling for that blown-out toaster sound.
        double preampDb = GetPreampDb(clampedIntensity);

        string guidLower = selectedMic.EndpointGuid.ToLowerInvariant();
        string guidUpper = selectedMic.EndpointGuid.ToUpperInvariant();

        // Equalizer APO compares the "Device:" filter against the endpoint string it is given, and
        // depending on version/APO host the casing of that endpoint id differs (Windows MMDevice ids
        // use an UPPER-case GUID, while some tools report lower-case). Emitting both forms in a single
        // semicolon-separated device filter makes the match case-insensitive.
        string deviceFilter = guidLower == guidUpper ? guidLower : $"{guidLower}; {guidUpper}";

        sb.AppendLine($"# Target Mic: {selectedMic.FriendlyName}");
        sb.AppendLine($"# Capture Endpoint GUID (both cases, for case-insensitive matching):");
        sb.AppendLine($"#   {guidLower}");
        sb.AppendLine($"#   {guidUpper}");
        sb.AppendLine($"Device: {deviceFilter}");
        sb.AppendLine("Channel: all");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Preamp: {preampDb:F1} dB"));

        if (enableToasterClipEq && clampedIntensity > 0.0)
        {
            double bassGain = Math.Round(10.0 + (normalized * 25.0), 1);    // +10 dB to +35 dB low-shelf rumble
            double lowMidGain = Math.Round(12.0 + (normalized * 28.0), 1);  // +12 dB to +40 dB boxy body
            double midGain = Math.Round(15.0 + (normalized * 35.0), 1);     // +15 dB to +50 dB harsh vocal crunch
            double highCut = Math.Round(-6.0 - (normalized * 12.0), 1);     // -6 dB to -18 dB toaster bandwidth roll-off

            sb.AppendLine("# 4-Stage Hard-Clip Overdrive EQ Shaping (forces digital clipping in 32-bit float WASAPI)");
            sb.AppendLine("Filter 1: ON HP Fc 85 Hz");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Filter 2: ON LSC Fc 160 Hz Gain {bassGain:F1} dB Q 0.70"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Filter 3: ON PK Fc 650 Hz Gain {lowMidGain:F1} dB Q 1.10"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Filter 4: ON PK Fc 1750 Hz Gain {midGain:F1} dB Q 1.15"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Filter 5: ON HSC Fc 4200 Hz Gain {highCut:F1} dB Q 0.70"));
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
