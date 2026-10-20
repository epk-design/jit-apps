# Tray Kanban

A kanban to-do board that lives in the Windows 11 system tray. Built with WPF on .NET 10 using the Fluent theme (Mica/Acrylic, light/dark), with an azure accent.

## Features
- **Several boards**, each with its own columns (To do / Doing / Done by default). You can add, rename, reorder or delete columns.
- **Cards** have a title, notes, a colour label, a due date, a checklist and attachments. Drag cards between columns.
- **Screenshots to text:** press Ctrl+V with a screenshot on the clipboard (from Win+Shift+S, for example). The image is attached and its text is read into the notes by the built-in Windows OCR, which works offline.
  - Pasting on the board creates a new card titled from the first line of the text.
  - Pasting inside a card adds the image to that card.
- **Attachments:** *Upload files* copies them into the board's folder. *Link a file* or *Add web link* stores a reference only. Dropping files onto a column or card copies them in; hold Alt while dropping to link them instead.
- **Tray:** left-click the icon opens the flyout, and right-click shows the menu. Win+Shift+K opens quick add from anywhere. The pin button keeps the flyout open, and you can drag it by the header.
- Starts with Windows (you can turn this off in Settings).

## Sharing with a team
Use **Settings → Change data folder…** and pick a folder that syncs for everyone, such as a OneDrive shared folder, a SharePoint library or a Teams channel's files synced in Explorer.
- Each board and each card is its own small JSON file, so people editing different cards never touch the same file.
- The app watches the folder and reloads when a teammate's change syncs in.
- Cards record who created them and who last updated them.
- If two people edit the *same* card at the same moment, the last save wins.

```
<data folder>/boards/<id>.json
<data folder>/cards/<id>.json
<data folder>/attachments/<card id>/<files>
```
Default data folder: `%APPDATA%\TrayKanban\data`. Settings are stored in `%APPDATA%\TrayKanban\settings.json`.

## Shortcuts
| Where | Keys |
|---|---|
| Anywhere | Win+Shift+K: quick add |
| Board | Ctrl+V: paste screenshot, text or files as a new card · Ctrl+F: search · Ctrl+N: add a card · Esc: hide |
| Card | Ctrl+V: paste screenshot or files · Ctrl+Enter or Esc: save and close |
| Quick add | Enter: add · Ctrl+Enter: add and open · Ctrl+V: screenshot |

## Build
Run `publish.bat`, which needs the .NET 10 SDK. It produces a single self-contained `publish\TrayKanban.exe`, so teammates don't need to install anything.
