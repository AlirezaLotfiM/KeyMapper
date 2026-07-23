# KeyMapper

KeyMapper is a modern C# / WPF desktop application for Windows that allows users to customize their keyboard and mouse inputs. It intercepts keyboard and mouse events globally using low-level Windows hooks, enabling powerful key mappings, auto-expanding text snippets, and system utility triggers.

## Features

- **Shortcut Replacements**: Map keyboard shortcuts to different keys, button inputs, or text values.
- **Auto-Expansion**: Automatically expand short abbreviations (e.g., `;addr`) into longer text strings as you type.
- **System Actions**: Bind shortcuts to trigger system utilities, such as:
  - `lock`: Instantly lock the Windows workstation.
  - `empty`: Empty the Recycle Bin silently.
  - `mute`: Toggle system volume mute.
  - `ip`: Query the local IPv4 address and copy it to the clipboard.
- **Process Filtering**: Target specific applications by specifying `AllowedProcess` or `ExcludedProcess` to ensure shortcuts only run (or never run) in specific active windows.
- **Overlay Notification Window**: A sleek, non-intrusive on-screen overlay that displays status messages and confirmations when actions are executed.
- **Command Palette**: Quickly search and execute system commands or hotkeys using a keyboard-driven palette interface.
- **Quick Access Window**: A convenient launcher style UI for rapid access.
- **Sound Effects**: Customizable audio cues when mappings or actions are triggered.
- **System Tray Integration**: Minimize the application to the Windows system tray to keep it running unobtrusively in the background.

## UI Design

KeyMapper features a modern, premium dark UI utilizing the Segoe UI font, sleek borders, smooth scrollbars, custom tab configurations, and dynamic hover effects.

## Getting Started

### Prerequisites
- Windows OS (7, 8, 10, or 11)
- .NET runtime (compatible with WPF apps)

### Running/Building
1. Clone the repository.
2. Open `KeyMapper.sln` or `KeyMapper.csproj` in Visual Studio.
3. Build and run in **Release** or **Debug** mode.

## Configuration

Configuration settings are stored locally in the user's `AppData\Local\KeyMapper\config.json`. The application automatically migrates older configuration schemas to the latest version on startup.
