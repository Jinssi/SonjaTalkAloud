# 🌸 Sonja Read Aloud

**Sonja** is a lightweight Windows app that reads your **selected text aloud** from almost any application — browsers, documents, email, PDFs, code editors — with a single global keyboard shortcut and high‑quality neural text‑to‑speech.

Select some text, press your shortcut, and Sonja speaks it. Press it again to stop. That's it. Sonja lives quietly in the notification area (system tray) and stays out of your way.

> Sonja is bring‑your‑own‑key: you connect it to your own text‑to‑speech provider. No accounts, telemetry, or credentials are bundled with the app — you stay in full control of your keys and costs.

---

## ✨ Features

- **Read selection aloud anywhere** — works across most Windows apps via UI Automation, with a clipboard fallback.
- **One global shortcut** — fully configurable (default **Ctrl + Alt + R**). Press once to read, again to stop.
- **Multiple TTS providers** — Azure AI Speech (including DragonHD / neural voices), Azure OpenAI TTS (Microsoft Foundry), or ElevenLabs.
- **Adjustable voice & speed** — pick any voice your provider offers and fine‑tune the speaking rate.
- **Runs in the tray** — closing the window keeps Sonja running; right‑click the tray icon for settings, a voice test, or exit.
- **Start with Windows** — optional auto‑start at sign‑in.
- **Private by design** — credentials live only in a local `.env` (or your own environment variables) and are never written to settings or logs.

---

## 🧩 How it works

Sonja reads configuration at startup and picks the first provider that is fully configured, in this order:

1. **Azure AI Speech** (preferred) — requires `SPEECH_KEY` + `SPEECH_REGION` (or `SPEECH_ENDPOINT`).
2. **ElevenLabs** — requires `ELEVENLABS_API_KEY`.
3. **Azure OpenAI TTS** (Microsoft Foundry) — requires `AZURE_OPENAI_TTS_*`.

This lets you keep Azure Speech as your main voice while having a fallback available. You only need **one** provider configured to use the app.

---

## 📋 Prerequisites

- **Windows 10 or 11**
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (only needed to build from source)
- **One text‑to‑speech provider** of your choice (see below)

---

## 🔑 Choose and set up a voice provider

You need credentials from **one** of the following. Pick whichever you prefer.

### Option A — Azure AI Speech (recommended)

Great neural voices, including the newer high‑definition DragonHD voices.

1. Create an **Azure AI Speech** (or multi‑service **Azure AI Services**) resource in the [Azure portal](https://portal.azure.com).
2. Open the resource → **Keys and Endpoint**.
3. Copy **Key 1** and the **Location/Region** (e.g. `westeurope`, `eastus`).
4. Put them in your `.env`:

   ```dotenv
   SPEECH_KEY=your-speech-key
   SPEECH_REGION=westeurope
   ```

   > If your resource uses a custom domain instead of a plain region, set `SPEECH_ENDPOINT=https://<your-resource>.cognitiveservices.azure.com/` as well.

To pick a voice, launch Sonja and click **Refresh voices** — the dropdown fills with every voice available on your resource. Example voice names: `en-US-AvaMultilingualNeural`, `en-US-Ava:DragonHDLatestNeural`, `en-GB-SoniaNeural`.

### Option B — ElevenLabs

1. Sign up at [elevenlabs.io](https://elevenlabs.io) and create an API key.
2. Add it to your `.env`:

   ```dotenv
   ELEVENLABS_API_KEY=your-elevenlabs-key
   ```

### Option C — Azure OpenAI TTS (Microsoft Foundry)

1. Deploy a TTS model (e.g. `tts` / `gpt-4o-mini-tts`) in your Azure OpenAI / Foundry project.
2. Add the endpoint, key, and deployment name to your `.env`:

   ```dotenv
   AZURE_OPENAI_TTS_ENDPOINT=https://your-resource.openai.azure.com/
   AZURE_OPENAI_TTS_KEY=your-key
   AZURE_OPENAI_TTS_DEPLOYMENT=your-tts-deployment
   AZURE_OPENAI_TTS_API_VERSION=2025-03-01-preview
   ```

---

## ⚙️ Configuration

Sonja reads settings from **process environment variables** or a **`.env` file** placed beside the executable (it also searches up to five parent directories).

1. Copy the template:

   ```powershell
   Copy-Item .env.example .env
   ```

2. Fill in the values for your chosen provider (see above).

### Environment variables

| Variable | Provider | Required | Description |
| --- | --- | --- | --- |
| `SPEECH_KEY` | Azure Speech | ✅ (for Azure Speech) | Azure AI Speech resource key |
| `SPEECH_REGION` | Azure Speech | ✅* | Resource region, e.g. `westeurope` |
| `SPEECH_ENDPOINT` | Azure Speech | ➖ | Custom‑domain endpoint (fallback when no region) |
| `ELEVENLABS_API_KEY` | ElevenLabs | ✅ (for ElevenLabs) | ElevenLabs API key |
| `AZURE_OPENAI_TTS_ENDPOINT` | Azure OpenAI TTS | ✅ (for Foundry) | Azure OpenAI endpoint |
| `AZURE_OPENAI_TTS_KEY` | Azure OpenAI TTS | ✅ (for Foundry) | Azure OpenAI key |
| `AZURE_OPENAI_TTS_DEPLOYMENT` | Azure OpenAI TTS | ✅ (for Foundry) | TTS deployment name |
| `AZURE_OPENAI_TTS_API_VERSION` | Azure OpenAI TTS | ➖ | Defaults to `2025-03-01-preview` |

\* Either `SPEECH_REGION` or `SPEECH_ENDPOINT` is required for Azure Speech.

> 🔒 **Never commit your `.env`.** It is already listed in `.gitignore`. Your keys stay on your machine and are never written to Sonja's settings file or logs.

---

## 🛠️ Build and run

```powershell
git clone https://github.com/<your-username>/sonja-read-aloud.git
cd sonja-read-aloud
Copy-Item .env.example .env   # then fill in your provider credentials

dotnet build Sonja.slnx --configuration Release
dotnet run --project Sonja.ReadAloud.csproj
```

### Publish a standalone executable

Produce a self‑contained build that runs without installing .NET:

```powershell
dotnet publish Sonja.ReadAloud.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/publish/win-x64
```

The app is at `artifacts/publish/win-x64/Sonja.ReadAloud.exe`. Place a private `.env` beside it (or define the environment variables system‑wide), then double‑click to run.

---

## ▶️ Usage

1. Launch `Sonja.ReadAloud.exe`.
2. In the settings window, choose your shortcut and voice, then **Save settings**.
3. Select text in any application.
4. Press the shortcut (default **Ctrl + Alt + R**) to start reading.
5. Press it again to stop.

Closing the window leaves Sonja running in the notification area. Right‑click the tray icon to reopen settings, test the voice, or exit. Enable **Start Sonja when I sign in to Windows** to launch it automatically at login.

---

## 🖱️ How selection capture works

Sonja first asks Windows **UI Automation** for the selected text. If an application doesn't expose a text‑selection provider, Sonja temporarily sends **Ctrl + C**, reads the copied text, and restores your previous clipboard contents.

Windows prevents a normal app from reading input from an elevated (administrator) app. If you need selection capture to work inside an elevated window, run Sonja at the same elevation level. Some DRM‑protected text intentionally cannot be copied or exposed through accessibility APIs.

---

## 🎨 Customizing the look

The pink‑and‑black theme is defined with a handful of brushes in [`App.xaml`](App.xaml) (`WindowBackgroundBrush`, `CardBrush`, `PrimaryBrush`, `TextBrush`, and friends). Change those color values to reskin the whole app. The header gradient and status colors live in [`MainWindow.xaml`](MainWindow.xaml) and [`MainWindow.xaml.cs`](MainWindow.xaml.cs).

To change the tray/app icon, replace [`Assets/sonja.ico`](Assets/sonja.ico) with your own `.ico`.

---

## 🤝 Contributing

Contributions are welcome! Please open an issue to discuss substantial changes first. For pull requests:

- Keep changes focused and match the existing code style.
- Run `dotnet build` and `dotnet test` before submitting.
- Don't commit secrets — verify `git status` shows no `.env`.

---

## 📄 License

Released under the [MIT License](LICENSE).

## 🙏 Credits

The tray and application icon is the cherry blossom glyph from [Twemoji](https://github.com/jdecked/twemoji), licensed under [CC‑BY 4.0](https://creativecommons.org/licenses/by/4.0/).
