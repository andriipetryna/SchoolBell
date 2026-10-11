# School Bell and Air Raid Alert

[Українська](README.md) | **English**

A Windows 10/11 app that plays lesson start and end bells on a schedule, a minute of silence, and air raid alert and all-clear signals for a selected community (hromada) or district.

The app's interface is in Ukrainian. In this document, interface labels are given in English with the original Ukrainian in parentheses.

## Features

- **Lesson schedule**: one schedule for all selected weekdays. The start bell rings when a lesson begins and the end bell when it ends.
- **Sounds**: mp3 (also wav, wma, m4a) are picked in the interface. The file is copied into the app's folder, so the original can be deleted.
  - Option "Different sounds for lesson start and end bells" («Різні звуки для дзвінка на урок і з уроку»). When enabled, the main tab shows two bell buttons.
- **Minute of silence**: its own file, time and days. It has priority: if it coincides with a bell, the bell rings right after it.
- **Air raid alert**: monitoring for a community or district via the "Air Raid Alert" (UkraineAlarm) API. Automatic alert and all-clear signals are enabled with a separate checkbox. An alert declared for the whole oblast or district is also taken into account. If the API reports a threat level, the alert card turns red or yellow and shows the reason. Bells ring as usual during an alert. No Windows notifications are sent: sound only.
- **Inactive periods**: days and time ranges when nothing plays automatically (bells, minute of silence, alerts).
- **Master switch** (holidays/vacations): turns off everything automatic, including alerts. The state persists across reboots.
- **Manual buttons**: bell (or two), minute of silence, alert, all-clear, stop. They always work.
- **"Today" feed** («Сьогодні») on the main tab fills the left side of the window down to the bottom; on the right are the alert card and the manual buttons. At the top is a status line: "Now playing" («Зараз звучить») with a "Stop" («Стоп») button, or "Next" («Наступне») with a countdown. Below is the day's timeline: all scheduled bells and the minute of silence (played / skipped and why / upcoming), manual plays, alerts, all-clears, alert level changes, and schedule on/off. A red marker shows the current time. Entries are stored in `C:\ProgramData\SchoolBell\journal\` (one file per day, 14 days), so they survive an app restart.
- **Preview** each sound on the "Sounds" («Звуки») tab.

## Reliability

| Situation | What happens |
|---|---|
| PC reboots | Task Scheduler starts the app at sign-in (Windows auto-logon required). |
| The app was closed or crashed | Task Scheduler checks every 5 min and starts it again. On a critical error the app restarts itself. |
| The window was closed | The window hides to the tray; the app keeps running. |
| The PC started a minute after a bell | The bell still rings (90 s grace period). Older bells are not played "late". |
| The clock was set back | The same bell will not ring twice. |
| An mp3 won't play | Falls back to MCI; if that fails too, a backup beep plays and a banner appears in the window. |
| The settings file is corrupted | Restored from the `settings.json.bak` backup, with a warning in the window. |
| No internet | Bells keep working; the window shows a "no connection to the alert service" banner. |
| Windows sleep | The app prevents sleep while it is running. |

## Installing on a school PC

1. Run `SchoolBell-Setup-1.2.1.exe` and follow the wizard. Administrator rights are required.
   The installer:
   - copies the app to `C:\Program Files\SchoolBell`;
   - sets up autostart: the "SchoolBell" Task Scheduler task starts the app at sign-in, checks every 5 min and restarts it if it was closed;
   - optionally disables PC sleep and blocks Windows Update restarts between 7:00 and 19:00;
   - creates shortcuts in the Start menu and (optionally) on the desktop;
   - registers the app in "Programs and Features";
   - launches the app right away.
2. Enable **Windows auto-logon** with [Sysinternals Autologon](https://learn.microsoft.com/sysinternals/downloads/autologon) (the link is on the installer's last page). A separate non-administrator user is recommended. After sign-in the screen can be locked (Win+L); sound will keep playing.
3. In the BIOS, set **Restore on AC Power Loss → Power On** so the PC turns itself back on after a power outage.
4. Open the app and configure:
   - "Schedule" («Розклад») tab: lessons, days, minute of silence;
   - "Sounds" («Звуки»): pick the files and preview them;
   - "Alert" («Тривога»): click "Update region list" («Оновити список регіонів»), choose oblast → district → community, then "Check now" («Перевірити зараз»);
   - "Inactivity" («Неактивність»): if needed;
   - click **"Save changes"** («Зберегти зміни»).

**Updating:** run the new `SchoolBell-Setup-*.exe` over the old one. Settings and sounds are kept.
**Uninstalling:** "Settings → Apps → Шкільний дзвінок → Uninstall". The installer asks whether to delete settings and sounds.
**Silent install** (e.g. on several PCs): `SchoolBell-Setup-1.2.1.exe /S`. All components are installed.

## Building the installer

The version is set in `src/SchoolBell.App/SchoolBell.App.csproj` (`<Version>`).

**macOS or Linux:**
```bash
brew install --cask dotnet-sdk   # .NET 10 SDK (Linux: https://dotnet.microsoft.com/download)
brew install makensis            # NSIS (Linux: sudo apt install nsis)
./scripts/build-installer.sh
```

**Windows:**
```powershell
winget install Microsoft.DotNet.SDK.10
winget install NSIS.NSIS
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

**GitHub Actions** (nothing to install locally): push the project to a GitHub repository, open Actions → "Build installer" → Run workflow. The finished installer appears under Artifacts. If you push a tag such as `v1.0.1`, the installer is attached to the Release.

Output: `dist/SchoolBell-Setup-<version>.exe` (about 60–70 MB). Nothing else needs to be installed on the school PC: .NET is bundled with the app.

The scripts first run the logic tests (`tests/SchoolBell.Core.Tests`) and stop if they fail.

## Alert API

- Without a key, the app uses the public mirror `siren.pp.ua`. It serves data from the official "Air Raid Alert" API with a limit of 50 requests per minute per IP. The app makes 4 requests per minute (15 s interval).
- For better reliability, get your own key at <https://api.ukrainealarm.com> and paste it on the "Alert" («Тривога») tab. Requests then go directly to `api.ukrainealarm.com`.
- After the app starts, the first value it receives is not treated as an "alert start". This way the siren won't sound again if the PC rebooted in the middle of an alert.
- The app is an additional warning channel and does not replace sirens or official procedures.

## Where things are

```
C:\Program Files\SchoolBell\SchoolBell.exe     the app
C:\ProgramData\SchoolBell\settings.json        settings (+ settings.json.bak)
C:\ProgramData\SchoolBell\sounds\              copies of sound files
C:\ProgramData\SchoolBell\regions.json         region list cache
C:\ProgramData\SchoolBell\journal\             daily event feed (kept for 14 days)
C:\ProgramData\SchoolBell\logs\error.log       hidden error log (up to 1 MB)
```

## Code structure

```
src/SchoolBell.Core/           Windows-independent logic (testable on any OS)
  Models.cs                    settings, lessons, inactive periods
  Schedule.cs                  daily events, inactivity, next event, DueEventTracker
  PlaybackCoordinator.cs       sound queue and priorities
  Alerts.cs, AlertMonitor.cs   UkraineAlarm API, region tree, alert state
  Storage.cs                   atomic JSON saving, sound import, error log
  Journal.cs, Timeline.cs      daily event journal and the "Today" feed
src/SchoolBell.App/            WPF app (UI built in code, no XAML)
  Program.cs                   single instance, restart
  App.cs, AppHost.cs           startup, timer, wiring everything together
  MainWindow.cs, Ui.cs         window and tabs
  AudioOutput.cs               MediaPlayer → MCI → backup beep
  TrayIcon.cs                  tray icon
tests/SchoolBell.Core.Tests/   38 tests: schedule, inactivity, sleep/clock changes, queue, API, event feed, storage
installer/                     SchoolBell.nsi (NSIS installer), setup-helper.ps1 (autostart, power)
scripts/                       build-installer.sh (macOS/Linux), build-installer.ps1 (Windows)
.github/workflows/             installer build on GitHub Actions
```

## Troubleshooting

- **No sound:** check the Windows volume and the default output device. Click "Preview" («Прослухати») on the "Sounds" («Звуки») tab.
- **Windows 10/11 "N" editions:** install the *Media Feature Pack* (Settings → Apps → Optional features), otherwise mp3 may not play.
- **The app doesn't start after a reboot:** check auto-logon and the "SchoolBell" task in Task Scheduler. Install log: `C:\ProgramData\SchoolBell\logs\install.log`.
- **Error details:** `C:\ProgramData\SchoolBell\logs\error.log`.
