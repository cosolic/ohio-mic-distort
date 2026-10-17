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
   Device: {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}
   Channel: all
   Preamp: 39.0 dB
   Filter 1: ON HP Fc 90 Hz
   Filter 2: ON LSC Fc 155 Hz Gain 14.4 dB Q 0.70
   Filter 3: ON PK Fc 1750 Hz Gain 17.7 dB Q 1.15
   Filter 4: ON HSC Fc 4300 Hz Gain -8.9 dB Q 0.70

   # Reset Device context so any subsequent Equalizer APO includes work normally
   Device: all
   ```
3. **Automatic `config.txt` Protection**: Automatically disables any accidental unscoped positive `Preamp:` lines in your main `config.txt` so your headphones are never boosted.
4. **Instant Reload & Clean Exit**: Equalizer APO's directory watcher detects changes to `ohio-mic-distort.txt` in **~10 ms** when you press the hotkey or drag the slider, and automatically resets your mic to normal volume when you toggle it off or close the app.

---

## Features

- **Big Distortion Toggle + Global In-Game Hotkey**: Toggle distortion on/off from inside any fullscreen game or Discord call (default hotkey: **`F8`**, click to rebind to any key or combo).
- **Single Intensity (Gain & Clip) Slider (`0%` – `100%`)**: Smoothly scales microphone Preamp from `+0 dB` to `+60 dB`.
- **"Toaster / Xbox 360 Mic" Hard-Clip EQ Shaping**: Combines low-end punch (`155 Hz` low-shelf boost) and harsh vocal overdrive (`1750 Hz` peaking boost + `4.3 kHz` high cut) to slam your microphone into crunchy digital clipping.
- **Read-Only Live Mic Input Meter**: Uses `WasapiCapture` to visualize your mic peak level in real time without playing any audio back through your headphones.
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
