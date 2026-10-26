# AudioSwitcher

A tiny Windows tray app that flips your default playback device with one hotkey,
instead of digging through the Sound flyout.

## Build

Double-click `build.bat`. It uses the C# compiler that already ships with Windows
(`.NET Framework 4.x`), so there's nothing to install. You get `AudioSwitcher.exe`
in the same folder.

## Use

Run `AudioSwitcher.exe`. A speaker icon appears in the tray (it may land in the `^`
overflow; drag it onto the taskbar to keep it visible).

| Action | What it does |
| --- | --- |
| **Ctrl+Alt+F12** | Switch to the next device in your cycle |
| Left-click tray icon | Same as the hotkey |
| Right-click → *Switch to* | Jump straight to any device |
| Right-click → *Hotkey cycles through* | Tick the 2+ devices you flip between (none ticked = all active devices) |
| Right-click → *Also switch call/chat device* | Also set the Communications default (Teams, Discord, etc.) |
| Right-click → *Start with Windows* | Adds/removes a `HKCU\...\Run` entry |

A small popup near the bottom of the screen shows which device you landed on.

## Settings

Stored in `%APPDATA%\AudioSwitcher\settings.txt`. *Edit settings* opens it in Notepad;
change `hotkey=` (e.g. `Win+Shift+A`, `Ctrl+Alt+Oemtilde`, any
[`Keys`](https://learn.microsoft.com/dotnet/api/system.windows.forms.keys) name)
and pick *Reload settings*.

## How it works

Windows has no public API to change the default audio device. The Sound control
panel uses the undocumented `IPolicyConfig` COM interface, and so does every
switcher tool (SoundSwitch, EarTrumpet, AudioDeviceCmdlets). This app calls
`SetDefaultEndpoint` on it for the Console and Multimedia roles (plus
Communications if enabled), and uses the regular `IMMDeviceEnumerator` API to list
devices.

## Licence

MIT. Free to use, copy, modify and share, including commercially. See [LICENSE](LICENSE). Provided as is, with no warranty.
