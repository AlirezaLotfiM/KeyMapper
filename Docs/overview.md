# Project Specification: Autonomous AI Desktop Companion & Workflow Automator (WPF Edition)

## 1. Executive Summary & Vision
This document outlines the technical specification and development architecture for a modular, AI-powered desktop automation agent designed as a virtual pet overlay ("The Green Gentleman").

Unlike traditional passive desktop widgets or simple sprite animations (e.g., Shimeji or Desktop Goose), this software bridges visual character interactivity with deep operating system-level automation, natural language processing (NLP), system-aware application execution, and real-time keyboard layout correction. By leveraging Windows Presentation Foundation (WPF), it acts as a natively integrated Windows desktop assistant capable of resolving multi-lingual typing friction, automating web and gaming environments, and serving as an on-demand translation helper—all while maintaining true per-pixel transparency and a minimal system footprint.

---

## 2. Core Feature Architecture

### 2.1. The Transparent Graphical Sprite Overlay (WPF & XAML)
* **Visual Presence:** A floating, frameless, transparent graphical character rendered using WPF's native Win32 layered window architecture (`AllowsTransparency="True"`, `WindowStyle="None"`).
* **Non-Intrusive Interaction:** Employs WinAPI Platform Invoke (`P/Invoke`) to dynamically toggle the `WS_EX_TRANSPARENT` extended window style. When in an "idle" state, mouse clicks pass directly through the sprite to desktop applications behind it. Interactivity is restored via global hotkeys, explicit mouse hovering, or dedicated click events.
* **State Machine & Animations:** Driven by a C# state machine and XAML Storyboards that transition seamlessly between behavioral states: *Idle*, *Sleeping*, *Listening*, *Working/Executing*, *Alerting/Pointing*, and *Talking*.

### 2.2. Multi-Lingual Reverse Keyboard-Layout Converter
One of the core productivity features is resolving accidental typing in the incorrect physical keyboard layout across **English (QWERTY)**, **German (QWERTZ)**, and **Persian/Farsi (ISIRI/Windows Persian)**.
* **The Problem:** A user typing in Persian layout while intending to type German produces meaningless character sequences (e.g., typing `اشممخ پثهد مثاقثق` instead of `Hallo mein Lehrer`).
* **The Solution:**
  1. **Hot-Key Capture:** Upon a global hotkey press (or interaction with the pet), the background hook simulates `Ctrl+C` to capture the currently highlighted text into the system clipboard.
  2. **Reverse Layout Mapping:** The engine maps the UTF-8 character sequence back to raw physical keyboard coordinates (scan codes).
  3. **Multi-Target Projection:** It projects those coordinates simultaneously across the alternate supported keyboard layouts (QWERTY, QWERTZ, and ISIRI).
  4. **N-Gram & Dictionary Validation:** A lightweight, offline statistical language detector evaluates the likelihood of each projection. If the German projection yields a valid grammatical sentence with high confidence while the original string is gibberish, the engine auto-selects the corrected text.
  5. **Instant Replacement:** The corrected text is injected into the clipboard, and a programmatic `Ctrl+V` replaces the text on screen in milliseconds.

### 2.3. System-Aware Application & Game Automation
The assistant acts as a natural language command interface for local operating system execution.
* **Zero-Brute-Force Discovery:** To avoid performance-heavy hard drive scans, the application utilizes .NET's `Microsoft.Win32.Registry` to dynamically query Windows Registry hives (`HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` and `HKEY_CURRENT_USER`), mapping installed applications and their exact executable paths.
* **Steam Library Integration:** Automatically parses local Steam configuration files (`libraryfolders.vdf` and `appmanifest_*.acf`) to detect installed games, game titles, and App IDs.
* **Direct URI Execution:** Launches Steam games instantly via system URI protocols using `System.Diagnostics.Process` (e.g., executing `steam://run/1091500` to launch *Cyberpunk 2077* without searching for executable binaries).
* **Smart Web & Installer Handling:**
  * Translates commands like `"open amazon"` into default browser navigation directed to the appropriate URL.
  * When a requested application or game is **not installed**, the assistant alerts the user and opens the official verification or store page (e.g., navigating to the Steam store page for downloading), guiding them through installation.

### 2.4. Conversational AI, Translation & Persistent Memory
* **LLM Tool Calling / Function Calling:** The backend integrates with large language models (via API or local runtimes like Ollama) utilizing structured Function Calling. Commands such as `"open Cyberpunk"` or `"translate this text to German"` route directly to executable C# or Python functions rather than generating plain text responses.
* **Contextual Pop-Up Translator:** Can be summoned to translate selected text strings (e.g., `"Ich hoffe, dass es dir besser geht. Gute Besserung!"`) into the user's desired language, displaying results in an interactive XAML-styled speech bubble next to the character sprite.
* **User Preference Persistence:** Uses a lightweight local SQLite database or JSON config store to retain user preferences, default languages, frequently used commands, and historical interactions across system reboots.

### 2.5. On-Screen Optical Character Recognition (OCR)
* **Visual Screen Snipping:** Allows the user to command the pet to "read" non-selectable text (e.g., inside an image, a video game cutscene, or a system error dialog).
* **Text Extraction & Routing:** Captures a bounded screen region using high-speed screen grabbing tools, processes the image through OCR (Windows Native OCR via `Windows.Media.Ocr` or Tesseract), and routes the extracted text to the translation or summarization pipeline.

---

## 3. Recommended Technology Stack & Frameworks

| Component | Recommended Technology | Justification & Advantages |
| :--- | :--- | :--- |
| **Desktop Overlay & UI** | **WPF (C# / .NET 8+)** | Native Windows layered window rendering, per-pixel transparency, hardware acceleration, and XAML vector/sprite animation support. |
| **System & OS Scripting** | **C# (.NET) / Python 3.11+** | C# natively handles Windows Registry (`Microsoft.Win32`) and URI execution; Python can be used as a backend microservice for LLM routing. |
| **Global Input & Hotkeys** | **WinAPI P/Invoke (User32.dll)** | Direct low-level Windows keyboard hooking for clipboard capture, hotkey registration, and click-through window styles. |
| **Language Detection** | `fasttext` / `langdetect` / `NTextCat` | Extremely fast offline language identification and dictionary checking for keyboard layout correction. |
| **App & Steam Discovery** | `Microsoft.Win32.Registry` / `Gameloop.Vdf` | Native .NET access to Windows Registry and Valve Data Format parsing for zero-lag application discovery. |
| **LLM & Function Routing** | `Semantic Kernel` / `LangChain` | Enables structured tool use, connecting natural language chat directly to local system execution methods. |
| **OCR & Screen Capture** | **Windows Native OCR (`Windows.Media.Ocr`)** | Built directly into Windows 10/11, requiring zero external heavy dependencies or Tesseract binary installations. |
| **Data Persistence** | `SQLite` / `Entity Framework Core` / JSON | Zero-configuration local database storage for user settings, language choices, and execution logs. |

---

## 4. Detailed Module Specifications & Reference Code Snippets

### Module A: Frameless Transparent Overlay (WPF XAML & C#)
To create a character that floats on the screen without standard window borders and supports dragging:

**MainWindow.xaml:**
```xml
<Window AllowsTransparency="True" Background="Transparent" Height="200" MouseDown="Window_MouseDown" ShowInTaskbar="False" Title="Green Gentleman Pet" Topmost="True" Width="200" WindowStyle="None" x:Class="DesktopPet.MainWindow" xmlns="[http://schemas.microsoft.com/winfx/2006/xaml/presentation](http://schemas.microsoft.com/winfx/2006/xaml/presentation)" xmlns:x="[http://schemas.microsoft.com/winfx/2006/xaml](http://schemas.microsoft.com/winfx/2006/xaml)">

    <Grid>
        <Image Source="Resources/pet_idle.png" Stretch="Uniform"/>
    </Grid>
</Window>
