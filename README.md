# Ohio Mic Distort (`ohio-mic-distort`)

A Windows 10/11 **C# / .NET 8 WPF** desktop app that turns your microphone into an absolutely horrendous auditory weapon for shouting at your friends in Discord calls and gaming lobbies—**while keeping your headphone/speaker audio 100% untouched**.

---

## Why Did Equalizer APO Boost Your Headphones Before?

When using Equalizer APO manually, two common pitfalls cause your **headphones** to get blasted alongside your microphone:

1. **Unscoped `Preamp:` rules in `config.txt`**: By default, Equalizer APO's `config.txt` applies filters to **all** audio devices (`Device: all`). Putting `Preamp: +30 dB` in `config.txt` without a `Device:` selector boosts your headphones and microphone simultaneously.
2. **Shared Headset Device Names**: Many USB/wireless gaming headsets use the **exact same product name** for both speakers and microphone (e.g., `HyperX Cloud II` or `Razer BlackShark V2`). Scoping Equalizer APO by name (`Device: HyperX Cloud II`) still matches **both** playback and capture!

### How `OhioMicDistort` Fixes This Completely

1. **Capture-Only Enumeration (`NAudio.CoreAudioApi`)**: The app queries Windows CoreAudio strictly for `DataFlow.Capture` (Microphone) endpoints. Playback/Headphone (`DataFlow.Render`) endpoints are never listed or targeted.
2. **Unique Capture Endpoint GUID Locking**: Every Windows audio endpoint has a globally unique GUID (`{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}`). The app writes its rules to `C:\Program Files\EqualizerAPO\config\ohio-mic-distort.txt` locked strictly to your microphone's **Capture Endpoint GUID**:
   ```text
   Device: {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}; {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}
   Channel: all
   Preamp: 63.8 dB
   Filter 1: ON HP Fc 85 Hz
   Filter 2: ON LSC Fc 160 Hz Gain 26.2 dB Q 0.70
   Filter 3: ON PK Fc 650 Hz Gain 30.2 dB Q 1.10
   Filter 4: ON PK Fc 1750 Hz Gain 37.8 dB Q 1.15
   Filter 5: ON HSC Fc 4200 Hz Gain -13.8 dB Q 0.70

   # Reset Device context so any subsequent Equalizer APO includes work normally
   Device: all
   ```
   Both the lower-case and UPPER-case endpoint GUID are emitted in one `;`-separated device filter, because
   Windows MMDevice ids report the GUID in upper case while some Equalizer APO hosts compare it in lower case.
3. **Automatic `config.txt` Protection**: Automatically disables any accidental unscoped positive `Preamp:` lines in your main `config.txt` so your headphones are never boosted, and always re-writes its `Include:` directive at the very bottom of the file preceded by a `Device: all` reset line — otherwise a `Device:` filter left active by your own rules would skip the include for the microphone entirely.
4. **BOM-Free Config Writes**: `config.txt` and `ohio-mic-distort.txt` are written as UTF-8 **without** a byte-order mark (`new UTF8Encoding(false)`), and any stray `U+FEFF` is stripped when reading. Equalizer APO's C++ parser folds a BOM into the first token, which silently kills line 1 of the file.
5. **Instant Reload & Clean Exit**: Equalizer APO's directory watcher detects changes to `ohio-mic-distort.txt` in **~10 ms** when you press the hotkey or drag the slider, and automatically resets your mic to normal volume when you toggle it off or close the app.

---

## Features

- **Big Distortion Toggle + Global In-Game Hotkey**: Toggle distortion on/off from inside any fullscreen game or Discord call (default hotkey: **`F8`**, click to rebind to any key or combo).
- **Single Intensity (Gain & Clip) Slider (`0%` – `100%`)**: Scales microphone Preamp from `+15 dB` to `+90 dB` — far past the `1.0` ceiling of the 32-bit float WASAPI capture buffers, so the signal is guaranteed to hard-clip instead of just getting louder.
- **"Toaster / Xbox 360 Mic" Hard-Clip EQ Shaping**: A 4-stage overdrive chain — `85 Hz` high-pass, `160 Hz` low-shelf (`+10 dB`..`+35 dB`), `650 Hz` (`+12 dB`..`+40 dB`) and `1750 Hz` (`+15 dB`..`+50 dB`) peaking boosts, plus a `4.2 kHz` high-shelf cut (`-6 dB`..`-18 dB`) — to slam your microphone into crunchy digital clipping.
- **Read-Only Live Mic Input Meter**: Uses `WasapiCapture(device, useEventSync: false, 40 ms)` to visualize your mic peak level in real time without playing any audio back through your headphones (polling capture works on endpoints where event-synchronous capture is unavailable once an APO chain is inserted).
- **"Is the APO actually enabled?" Detector**: Reads `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\{endpoint guid}\FxProperties` for Equalizer APO's CLSIDs (`{EACD2258-FCAC-4FF4-B36D-419E924A6D79}`, `{EC1CC9CE-FAED-4822-828A-82A81A6F018F}`) and shows a warning banner on the selected microphone if the APO is not registered on it — the usual reason "nothing sounds distorted".
- **One-Click Folder Permission Helper**: Grants standard user write access to `C:\Program Files\EqualizerAPO\config` once so you never have to run the app as Administrator.

---

## Prerequisites & One-Time Setup (Windows 10 / 11)

1. Install **[Equalizer APO](https://sourceforge.net/projects/equalizerapo/)**.
2. Open Equalizer APO's **Device Selector / Configurator** (or click **"Open APO Device Selector"** inside Ohio Mic Distort):
   - Switch to the **Capture devices (Microphones)** tab.
   - Check the box next to your **Microphone** (you can **uncheck** your Headphones under Playback devices if you don't use EQ on your headphones).
   - Reboot Windows once if Equalizer APO prompts you to complete driver installation.
3. In **Discord** (`User Settings → Voice & Video`), turn **OFF** *Krisp Noise Suppression*, *Echo Cancellation*, and *Automatic Gain Control* when you want full earrape distortion (otherwise Discord's noise gate will try to clamp loud clipping).

---

## Building & Running on Windows

### Option 1: .NET 8 CLI

Requires the [.NET 8.0 SDK (Windows)](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Run directly in Debug mode
dotnet run --project src/OhioMicDistort/OhioMicDistort.csproj

# Publish a standalone single-file Windows executable (.exe)
dotnet publish src/OhioMicDistort/OhioMicDistort.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

### Option 2: Visual Studio 2022

1. Open `OhioMicDistort.sln` in Visual Studio 2022 (with the **.NET desktop development** workload installed).
2. Press **F5** to build and launch.

---

## Project Structure

```text
ohio-mic-distort/
├── OhioMicDistort.sln
└── src/
    └── OhioMicDistort/
        ├── OhioMicDistort.csproj          # .NET 8 WPF + NAudio 2.2.1
        ├── app.manifest                   # Windows 10/11 compatibility manifest
        ├── App.xaml / App.xaml.cs         # Dark theme resources & entry point
        ├── MainWindow.xaml / .cs          # Main UI, slider, hotkey binder, live rule preview
        ├── Models/
        │   ├── AppSettings.cs             # Persisted settings (%APPDATA%\OhioMicDistort\settings.json)
        │   └── MicDeviceInfo.cs           # Capture endpoint model & GUID representation
        └── Services/
            ├── AudioDeviceService.cs      # NAudio Capture-only enumeration & WASAPI level meter
            ├── EqualizerApoService.cs     # Scoped ohio-mic-distort.txt writer & headphone protection
            ├── GlobalHotkeyService.cs     # Win32 RegisterHotKey global in-game hotkey hook
            └── SettingsService.cs         # JSON settings persistence
```
