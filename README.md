<p align="center">
  <img src="Resources/app_icon.png" alt="KeyMapper Desktop Pet" width="112">
</p>

<h1 align="center">KeyMapper Desktop Pet</h1>

<p align="center">
  <strong>A lively pixel-art companion for Windows that can talk, translate, see, help, and groove.</strong>
</p>

<p align="center">
  <a href="https://www.microsoft.com/windows"><img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows"></a>
  <a href="https://dotnet.microsoft.com/"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet"></a>
  <a href="#support-the-project"><img alt="Free desktop companion" src="https://img.shields.io/badge/free-desktop%20companion-55CAD3"></a>
  <a href="#optional-local-ai"><img alt="Local optional AI" src="https://img.shields.io/badge/AI-local%20and%20optional-69B98E"></a>
</p>

<p align="center">
  <a href="#highlights">Highlights</a> ·
  <a href="#build">Build</a> ·
  <a href="#color-themes">Themes</a> ·
  <a href="#support-the-project">Support</a>
</p>

> [!NOTE]
> KeyMapper is not a floating chatbot skin. Its companions have distinct voices,
> movement, reactions, music behavior, and a set of practical Windows tools.

KeyMapper combines a character-driven assistant, local music library, bilingual
conversation, multilingual OCR, live translation, text expansion, application
actions, reminders, and keyboard-layout repair in one friendly Windows experience.

| Talk naturally | Understand intent | Stay in control |
| :---: | :---: | :---: |
| Persian or English | Local AI is optional | Consequential actions ask first |

## Why KeyMapper feels different

| Character-first | Useful every day | Private by design |
| --- | --- | --- |
| Distinct personalities, reactions, movement, and conversation styles | OCR, translation, shortcuts, music, reminders, and app launching | Local configuration, opt-in downloads, and local background AI |

## Highlights

### Three distinct desktop companions

- **Pip** is energetic, curious, and playful.
- **Professor Owlet** is calm, analytical, and precise.
- **Dude** is direct, practical, and dryly funny.

Characters can walk around the desktop, remain in one place, react to active applications and music, and speak at a Quiet, Normal, or Chatty frequency. Walking speed and idle-animation speed are independent controls.

Movement can be disabled, roam freely, or be locked to a strict horizontal line. A pet placed on the taskbar edge keeps its ground position when walking begins instead of snapping to a hidden movement boundary. Speech and hover music controls stay inside the working area when the pet is placed near a screen edge.

### Theme-aware music studio

The local music player is part of the companion experience, not a separate skin:

- It inherits the active KeyMapper palette, including light themes.
- **Cyber Groove** preserves the violet/magenta/cyan studio look as an optional dark theme.
- Full and mini players share the same visual language as the control center, pet controls, speech bubble, and context menu.
- The mini player turns the current cover into an edge-to-edge cinematic backdrop with subtle grain, slow artwork drift, glass controls, theme accents, and a proper vector maximize control. Mini-player hover states change opacity without painting distracting blocks behind the buttons.
- Supplied vector-style icons are used for folders, playback, shuffle, repeat, volume, and library navigation.
- Every playing track gets a live, track-seeded groove animation.
- Queue, favorites, history, artists, genres, playlists, sorting, and folder management are available locally.
- Overlapping music folders and repeated metadata no longer create duplicate library rows.
- Refreshing music folders preserves the current stream, position, play/pause state, and groove animation.
- Library rows show a clean duration column without play counters or scrollbar clipping.
- Favorites use a solid heart whose fill follows the selected theme.
- Song titles use the active theme's text color, so the library remains readable in both light and dark palettes.
- Repeat clearly cycles through three supplied icons: Off, Repeat all, and Repeat one.
- Numeric ID3 genre codes such as `(13)Pop` are normalized to readable names; the separate line below each genre is the real track count.
- The player keeps a clean border instead of a large neon halo.

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

Local AI is also memory-conscious:

- Installed models are not loaded when **Use the downloaded model for conversations** is off.
- A loaded model is released 45 seconds after the last reply.
- Closing **Talk with character** releases its native model memory immediately.
- CPU inference is capped at six threads with a compact 2,048-token context and smaller batch to keep the rest of Windows responsive.
- Music cover scanning reuses pooled buffers instead of allocating a new multi-megabyte buffer for every track.

The model download size is also a useful approximation of the minimum weight memory used while that model is answering. Larger models still need additional working memory during inference.

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
- On first use, the translator can install its own private LibreTranslate runtime under `%LOCALAPPDATA%\KeyMapper\Translation`; no administrator access, Docker, or system-wide Python installation is required.
- Nothing is downloaded until the user confirms **Install local translator**. Setup progress, Retry/Repair, and Remove controls are available in the translator's Settings panel.
- The local runtime and English, German, and Persian models use roughly 1 GB of disk space. Translation text stays on the computer when the local service is selected.
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
- Cyber Groove

Theme location:

```text
Settings → Appearance
```

The selected theme is saved locally and applies immediately to the control center, conversation window, translator, OCR results, music player, pet mini-player, speech bubble, and pixel-style context menu. The Settings page uses eased mouse-wheel scrolling, equal-width palette previews, and two-column AI model cards with clear download and RAM labels.

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

LibreTranslate runtime data is also kept outside the repository under `%LOCALAPPDATA%\KeyMapper\Translation`. The first-use installer downloads the official Python embeddable runtime, pip bootstrap, and LibreTranslate package, then keeps its dependencies and language models isolated from other Windows users and Python installations. API keys are stored only in the local configuration file and are never committed intentionally.

## Main technologies

- C# and WPF
- LLamaSharp and llama.cpp
- Official Qwen3 and Qwen2.5 GGUF models
- Tesseract OCR
- LibreTranslate
- Windows global keyboard and mouse hooks
- Windows Global System Media Transport Controls

## Design principles

- **Characters, not chat skins:** personality changes word choice, rhythm, reactions, and suggestions.
- **Visible state:** the UI says what is local, hosted, offline, listening, or waiting.
- **No surprise downloads:** AI and translation runtimes require an explicit choice.
- **Safe actions:** consequential operations ask first and never claim success without evidence.
- **One visual system:** themes, Segoe UI, icons, spacing, and pet surfaces belong to the same product.

## Project status

KeyMapper is under active development. The desktop tools, pets, local-AI model manager, OCR workflow, translator, tray behavior, and personalization controls are functional. PixelYar Cloud integration is explicitly marked as preview until its stable desktop API is available.
