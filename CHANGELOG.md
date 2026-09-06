# Changelog

## 1.0.0 - 2026-09-06

First release.

### The wrapper

- Self-contained single `.exe` built on WinForms + WebView2. No .NET install needed,
  no Electron, no Node.
- Reads `<exename>.conf` from the exe's own folder. A missing, unreadable or invalid
  file gives a modal error box and exit code 2.
- The exe can be renamed freely: `mstodo.exe` reads `mstodo.conf` and writes
  `mstodo.session`. One build serves any number of wrapped apps.
- Config is JSON with `//` and `/* */` comments and trailing commas allowed. It may be
  plain text or AES-256-GCM encrypted; the app detects which from the file header.
- Invalid setting values produce a warning box and fall back to defaults instead of
  stopping the app.

### Window

- `window-type`: `""` for a frameless window, or any mix of `min`, `max`, `close`,
  `tray`. Real native Windows title bar and button order. Leaving out `close` makes
  Windows draw a properly greyed-out X and blocks Alt+F4.
- `monitor` follows the `\.\DISPLAYn` numbering shown in Windows Settings, and falls
  back to the primary monitor when the number does not exist.
- `position`: `center`, `last` (remembered in the `.session` file), or `x,y`.
- `window-state`: `normal`, `min`, `max`, `tray`. `tray` never draws the window, not
  even for a frame, and does not start the browser until the window is first opened.
- `resizable`, `always-on-top`, `zoom`, `user-agent`, `context-menu`, `dev-tools`.

### Tray

- `systray`, plus the `tray` tag, plus `window-state: tray` all switch the tray on.
- Menu: Open, optional Open in browser, Exit.
- With the tray on, the X hides the window instead of quitting, the way most tray apps
  behave. `close-action` can force either behaviour.
- `minimize-to-tray` decides whether Minimize also hides the window.

### Memory

- `sleep-after`: after the window has been minimized or hidden for the given time, the
  whole WebView2 process tree is shut down and the working set trimmed. Opening the
  window rebuilds the browser on the same page. Measured drop: ~57 MB to ~10 MB.
- `auto` mode watches free RAM: under 20 % free it sleeps after 10 minutes, otherwise
  after 120 minutes.
- A window open on the desktop is never put to sleep.

### One instance per exe file

- The lock is keyed on the full exe path, so the same file cannot start twice, while
  copies in different folders run side by side with separate browser profiles.
- `single-instance-action`: `error` (message box, exit code 3) or `focus` (quietly
  bring the running window back, even from the tray or from sleep).

### Tools

- `encode.exe` / `decode.exe`: AES-256-GCM with PBKDF2-HMAC-SHA256 (310 000
  iterations), authenticated so a wrong password is reported rather than producing
  rubbish. In-place with a `.bak`, or to a separate output file.
- `setup.exe`: detects the WebView2 runtime and downloads plus installs it when
  missing. `--silent` for unattended use.
- If the runtime is missing at start-up, the app offers the download link and lets the
  user point at a fixed-version runtime folder, which is remembered.
- `tools/smoke-test.ps1`: 57 checks against the real built exe.
- `tools/make-icon.ps1`: draws the icon, so no image editor is needed to build.
