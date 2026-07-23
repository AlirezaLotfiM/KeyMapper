# KeyMapper Desktop Pet

KeyMapper is a Windows productivity companion that combines a pixel-art desktop pet with text expansion, application actions, multilingual OCR, translation, keyboard-layout repair, and optional AI conversation.

The pet is designed to feel like a character rather than a floating toolbar. Each character has its own speaking style, movement rhythm, reactions, suggestions, and music comments.

## Highlights

### Three distinct desktop companions

- **Pip** is energetic, curious, and playful.
- **Professor Owlet** is calm, analytical, and precise.
- **Dude** is direct, practical, and dryly funny.

Characters can walk around the desktop, remain in one place, react to active applications and music, and speak at a Quiet, Normal, or Chatty frequency. Walking speed and idle-animation speed are independent controls.

### Conversation and computer actions

Talk to the selected character in Persian or English. Deterministic computer actions are handled locally and separately from natural-language conversation.

Examples:

- Open an installed application.
- Find and launch an installed Steam game.
- Explain when an application or game is missing.
- Offer the official website, store page, or trusted installation route.
- Continue a personality-aware conversation without pretending an action succeeded.
- Meet each character in a portrait-led conversation space with live mood, thinking feedback, contextual prompt suggestions, and screen-awareness status.
- Keep up to 12 recent exchanges as private local memory for each character, with a two-step **New chat** control that can forget them instantly.
- Experience a genuinely different conversational rhythm, humor, emotional response, Persian register, and point of view from Pip, Professor Owlet, and Dude.

Open conversation from either the pet’s **Talk with Character** menu item or the **Talk with character** button in the control-center header.

Software installation and other consequential actions require explicit confirmation.

### Optional local AI

The program recommends a model based on available memory and CPU threads. A model is **never downloaded automatically**.

| Choice | Model | Download | Intended use |
| --- | --- | ---: | --- |
| Lite | Qwen3 0.6B Q8 | 639 MB | Modest PCs and basic Persian/English conversation |
| Balanced | Qwen3 1.7B Q8 | 1.83 GB | Better personality and conversation on most modern PCs |
| Quality | Qwen3 4B Q4_K_M | 2.5 GB | More nuanced replies on systems with generous memory |
| Classic | Qwen2.5 3B Instruct Q4_K_M | 2.1 GB | Direct, concise instruction-following as an alternative to Qwen3 |
| Pro | Qwen3 8B Q4_K_M | 5.03 GB | Richer bilingual conversation on powerful desktops |
| Max | Qwen3 14B Q4_K_M | 9 GB | Highest local quality for high-memory systems |

Downloaded models are stored under:

```text
%LOCALAPPDATA%\KeyMapper\Models
```

They can be removed from Settings at any time. Inference uses [LLamaSharp](https://github.com/SciSharp/LLamaSharp) with a CPU backend for broad Windows compatibility. The selected official [Qwen GGUF](https://huggingface.co/Qwen) model runs locally after download.

### PixelYar Cloud preview

Users who do not want to download a model can open the hosted [PixelYar Cloud chat preview](https://chat-agent.alirezalotfi.workers.dev/).

The Cloudflare-hosted service is still under development and currently uses its own protected web-chat connection. The desktop application labels it as **Preview** and does not pretend that the in-app API connection is finished. A custom OpenAI-compatible endpoint can also be configured in Advanced settings.

Background AI comments never call the hosted or custom service. They use only a downloaded local model and only receive:

- the active window title; or
- the current music title and artist.

The feature has its own on/off control.

### Multilingual text tools

- OCR for Persian, English, and German screen regions.
- Multi-pass OCR preprocessing and confidence reporting.
- Copy or translate recognized text from the OCR result window.
- Live translation with automatic source detection through LibreTranslate.
- Persian translation normalizes common colloquial spelling and preserves technical acronyms such as SVG, JSON, API, OCR, and PDF.
- De-gibberish text typed with the wrong Persian, English, or German keyboard layout.
- Translate selected text without leaving the current application.

### Text expansions and application actions

- Expand abbreviations into longer text.
- Insert `{date}`, `{time}`, `{clip}`, `{sel}`, and `{cursor}` values.
- Limit mappings to allowed applications.
- Disable mappings in excluded applications.
- Launch configured programs and utility actions.
- Search actions through the command palette.

### System tray and startup

Closing the control center keeps KeyMapper available in the Windows system tray. The tray icon can restore or hide the pet, open settings, enable or disable mappings, and exit the program.

Automatic startup is **off by default**. Change it here:

```text
Settings → Everyday Settings → Launch the desktop pet when I sign in to Windows
```

Uncheck it at any time to opt out. The setting affects only the current Windows user.

### Color themes

The interface uses Segoe UI and includes light, dark, and colorful palettes:

- Warm Cream
- Sky Paper
- Soft Mint
- Midnight Pixel
- Graphite Gold
- Sunset Arcade

Theme location:

```text
Settings → Appearance
```

The selected theme is saved locally and applies immediately to the control center, conversation window, translator, OCR results, pet speech bubble, and pixel-style context menu. The Settings page uses eased mouse-wheel scrolling, equal-width palette previews, and two-column AI model cards with clear download and RAM labels.

The interface uses a shared icon set for tabs, settings sections, model actions, and the pet context menu. The Windows application and tray icon use the purple desktop-pet mark rather than the old gray placeholder. The character also uses a compact two-layer contact shadow positioned directly beneath its feet. Form connectors are centered precisely between their related input fields.

### Support the project

KeyMapper Desktop Pet is free to use. The **Support** tab includes optional donation details and one-click address copying for anyone who wants to help fund continued development. Donations do not unlock features, change support priority, or send information from the app.

| Asset | Network | Address |
| --- | --- | --- |
| Bitcoin (BTC) | BEP20 / BNB Smart Chain | `0x45ECCb5357132A077eE3a717fA7D5D2F30C1E2A9` |
| Tether (USDT) | BEP20 / BNB Smart Chain | `0x45ECCb5357132A077eE3a717fA7D5D2F30C1E2A9` |
| TRON (TRX) | TRC20 / TRON | `TKMzF6JU5CjSoVq88oRaXnd6Ye7RUAscL1` |
| Toncoin (TON) | TON | `UQCOxNWxA84XKNlNMDJ-GREgcaG_wMtm-e6r6fcVpIKvXTai` |
| Ethereum (ETH) | ERC20 / Ethereum | `0x45ECCb5357132A077eE3a717fA7D5D2F30C1E2A9` |

Always select the exact network shown above and verify the asset, network, and address in your wallet before sending. Transfers made on the wrong network may be permanently lost.

## Build

### Requirements

- Windows 10 or Windows 11
- .NET 10 SDK
- Visual Studio 2022 or a compatible command-line environment

### Command line

```powershell
git clone https://github.com/AlirezaLotfiM/KeyMapper.git
cd KeyMapper
dotnet restore
dotnet build -c Release
dotnet run
```

The project targets:

```text
net10.0-windows10.0.19041.0
```

## Local data

Configuration:

```text
%LOCALAPPDATA%\KeyMapper\config.json
```

Optional local AI models:

```text
%LOCALAPPDATA%\KeyMapper\Models
```

LibreTranslate runtime data is also kept outside the repository. API keys are stored only in the local configuration file and are never committed intentionally.

## Main technologies

- C# and WPF
- LLamaSharp and llama.cpp
- Official Qwen3 and Qwen2.5 GGUF models
- Tesseract OCR
- LibreTranslate
- Windows global keyboard and mouse hooks
- Windows Global System Media Transport Controls

## Project status

KeyMapper is under active development. The desktop tools, pets, local-AI model manager, OCR workflow, translator, tray behavior, and personalization controls are functional. PixelYar Cloud integration is explicitly marked as preview until its stable desktop API is available.
