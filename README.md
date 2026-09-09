# 🌸 Sonja Read Aloud

Read the **text you've selected** aloud in any Windows app with one keyboard shortcut. Press once to read, again to stop. Sonja sits in the system tray and speaks with your own Azure AI Speech key.

![Sonja settings window](Assets/screenshot.png)

## Features

- Reads the current selection anywhere (UI Automation, with a clipboard fallback).
- One global shortcut, fully configurable (default **Ctrl + Alt + R**).
- **Attitude** — Azure neural speaking styles (friendly, cheerful, empathetic, excited, newsreader…) with adjustable intensity, so it sounds natural instead of flat.
- Recommended natural voices (★) and style‑capable voices (· styles) surfaced first.
- Adjustable voice and speed; optional start with Windows.
- Bring‑your‑own‑key: no accounts or telemetry. Paste your Azure Speech key in the app — it's stored encrypted for your Windows account (DPAPI).

## Quick start

1. **Get an Azure Speech key.** You need an Azure AI Speech resource of your own (the app ships none):
   - Sign in with an [Azure account](https://azure.microsoft.com/free/) — the **free F0** Speech tier is plenty. No subscription? Create one (the free account includes credit).
   - **Either** create the resource in the [Azure portal](https://portal.azure.com): **Create a resource** → search **Speech** → **Create** (pick a subscription, resource group, and region), then open it and go to **Keys and Endpoint**.
   - **Or** use [Speech Studio](https://speech.microsoft.com) → sign in → it can create/select a Speech resource for you; open your resource's **Keys and Endpoint** to see the key.
   - Copy **Key 1** and the **Region/Location** (e.g. `westeurope`, `eastus`).
2. **Run Sonja** (grab a published build, or from source: `dotnet run --project Sonja.ReadAloud.csproj`).
3. **Connect.** In the **Azure Speech connection** panel, paste your **Key** and **Region** (e.g. `westeurope`) and click **Save & connect**. Your key is saved encrypted — no rebuild or `.env` needed.
4. Click **Refresh voices**, pick a voice and attitude, then **Save settings**.

> **Prefer a file?** You can instead put `SPEECH_KEY` and `SPEECH_REGION` in a `.env` beside the executable (copy `.env.example`). A key entered in the app takes precedence.

> **Sharing the repo?** Everyone brings their own key — the repo ships none. Send the source or a published build; each person enters their own key in the app (or their own `.env`). Never commit `.env` (it's in `.gitignore`); rotate a key if it ever leaks.

## Voices & attitude

Click **Refresh voices** to load every voice on your resource. Recommended natural voices appear first (★); voices tagged **· styles** support attitude.

| Voice | Character | Styles |
| --- | --- | --- |
| `en-US-Ava:DragonHDLatestNeural` | Ultra‑natural HD (default) | – |
| `en-US-AvaMultilingualNeural` | Natural, multilingual | – |
| `en-US-AriaNeural` | Versatile, widest style set | ✅ |
| `en-US-JennyNeural` | Warm assistant | ✅ |
| `en-US-SaraNeural` | Youthful and lively | ✅ |
| `en-GB-SoniaNeural` | British English | ✅ |

Pick an **Attitude** (e.g. *Cheerful*, *Empathetic*, *Newsreader*) and set **Intensity** (0.5–2.0×). DragonHD voices sound best plain but ignore styles — use a **· styles** voice such as Aria or Jenny for attitude.

## Trigger it from a mouse button

Multi‑button mice can fire the shortcut, so you can read a selection without touching the keyboard:

1. Open your mouse software (Logitech Options+, Razer Synapse, Microsoft Mouse and Keyboard Center, etc.).
2. Select a spare button and assign a **keystroke / hotkey**.
3. Record Sonja's shortcut (default **Ctrl + Alt + R**) and save.

No vendor software? Map a button to the keys with [AutoHotkey](https://www.autohotkey.com/). That button now reads or stops the selection.

## Build & publish

```powershell
dotnet build Sonja.slnx -c Release                                # build
dotnet test                                                       # run tests
dotnet publish Sonja.ReadAloud.csproj /p:PublishProfile=Win-x64   # self-contained win-x64
```

The published app is at `artifacts/publish/win-x64/Sonja.ReadAloud.exe`; keep a private `.env` beside it.

## Notes

- Selection capture uses UI Automation, falling back to a brief **Ctrl + C** (your clipboard is restored). Reading from an elevated window needs Sonja running at the same elevation.
- Other providers work too if configured instead of Azure Speech: ElevenLabs (`ELEVENLABS_API_KEY`) or Azure OpenAI TTS (`AZURE_OPENAI_TTS_*`).
- Theme colors live in [`App.xaml`](App.xaml); replace [`Assets/sonja.ico`](Assets/sonja.ico) to change the icon.

## License

[MIT](LICENSE). Icon: cherry blossom from [Twemoji](https://github.com/jdecked/twemoji) (CC‑BY 4.0).

