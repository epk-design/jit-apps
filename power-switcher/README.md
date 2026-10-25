# PowerSwitcher

A tiny Windows tray app that cycles your power plan with one hotkey, instead of
digging through Control Panel. Sibling of AudioSwitcher, same build-with-nothing
approach.

## Build

Double-click `build.bat`. It uses the C# compiler that already ships with Windows
(`.NET Framework 4.x`), so there's nothing to install. You get `PowerSwitcher.exe`
in the same folder.

## Use

Run `PowerSwitcher.exe`. A lightning-bolt icon appears in the tray (it may land in
the `^` overflow; drag it onto the taskbar to keep it visible).

| Action | What it does |
| --- | --- |
| **F13** (default) | Switch to the next power plan / mode |
| Left-click tray icon | Same as the hotkey |
| Right-click → *Power plan* / *Power mode* | Jump straight to any plan or mode |
| Right-click → *Hotkey cycles through* | Plans, Windows 11 power modes, or Automatic; tick the plans to include (none ticked = all) |
| Right-click → *Change hotkey...* | Press any combo (e.g. `Ctrl+Alt+F11`) and click OK |
| Right-click → *Start with Windows* | Adds/removes a `HKCU\...\Run` entry |

A small popup near the bottom of the screen shows where you landed.

### Plans vs. modes

Many Windows 11 machines (especially laptops with Modern Standby) only expose the
*Balanced* plan and put the real choice in Settings → Power → *Power mode*
(Best power efficiency / Balanced / Best performance). In *Automatic*, the hotkey
cycles plans when you have two or more, and power modes otherwise.

## Settings

Stored in `%APPDATA%\PowerSwitcher\settings.txt` (`hotkey=`, `cycle=auto|plans|modes`,
`showOsd=`, `plan=<guid>` lines). *Edit settings file* opens it in Notepad; pick
*Reload settings* after editing.

## How it works

Plans use the documented `powrprof.dll` API (`PowerEnumerate`,
`PowerGetActiveScheme`, `PowerSetActiveScheme`). Power modes are "overlay schemes"
on top of the active plan, set with the undocumented
`PowerGetEffectiveOverlayScheme` / `PowerSetActiveOverlayScheme` exports that the
Settings app itself uses. Neither needs admin rights.
