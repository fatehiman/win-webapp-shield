# Changelog

## 1.4.0 - 2026-09-06

### Added

- `set-icon.exe`. Put an `.ico` next to a built exe of the same name, run it, and the
  exe carries that icon. No rebuild, no .NET SDK, no source. Run it with no arguments
  and it takes the first `.ico` in its own folder and the exe with the matching name,
  so setting up a new wrapped app is: rename the exe, drop the icon in, double click.

### Notes

- The icon is written straight into the exe with the Windows resource API, which
  rewrites the program and moves the end of the file. A .NET single file exe keeps the
  whole app appended after that end, addressed by offsets counted from the start of
  the file, so a plain resource edit leaves it with "Failure processing application
  bundle". `set-icon` therefore takes the appended block off first, lets Windows write
  the icon, puts the block back, and moves every offset inside it by the same amount.
- It refuses to touch an exe whose appended data it cannot recognise, for example a
  code signed one, and it keeps a copy next to the exe until the whole job is through.
- Every icon already in the exe is removed, so the new one cannot be shadowed by an
  old one with a lower resource id.

## 1.3.0 - 2026-09-06

### Added

- While the window is being resized, its size is shown after the title
  (`MS To Do - 450x320`) and clears itself one second after the last change. The
  numbers are the whole window, the same thing `width` and `height` mean in the
  config, so a size you like can be read off the title bar and pasted straight in.
- `show-size-on-resize` turns it off.

### Notes

- Maximizing or snapping shows the size too, since that also changes it. Restoring
  from the tray or from minimized does not, because the size did not actually change,
  and re-announcing an unchanged size would just be noise.
- The window title now has three shapes: `"<title>"` when idle,
  `"<title>  <frame>"` while loading, and `"<title> - <w>x<h>"` while resizing. It is
  built in one place so the spinner and the size cannot overwrite each other; the size
  wins while it is showing, being the shorter lived of the two.

## 1.2.0 - 2026-09-06

### Added

- `tools/make-app-icon.ps1`: turns an SVG, PNG, JPG, BMP or existing `.ico` - from a
  local file, an http(s) URL, or a `-Preset` name - into a proper multi-size Windows
  `.ico` (16, 20, 24, 32, 40, 48, 64, 128, 256 px). `-Padding` leaves room around a
  logo drawn edge to edge, `-Background` fills the transparent area, `-Sizes` picks
  which sizes go in.
- `-Preset` covers the Microsoft 365 product logos that Microsoft publishes as SVG on
  its own Fluent brand-icon CDN: todo, outlook, teams, onenote, excel, word,
  powerpoint, onedrive, sharepoint, access, visio, project, forms, sway, stream,
  delve, yammer, loop. `-ListPresets` prints them.
- `build.ps1 -AppIcon <file.ico>`: compiles that icon into the named copy of the
  wrapper as a Win32 resource, copies the `.ico` next to the exe, and points the
  generated `.conf` at it - so Explorer, the taskbar, Alt+Tab, the window title bar
  and the tray all show the right icon. Needs `-AppName`.
- `docs/ICONS.md`, and `tools/lib/IcoWriter.ps1` which writes and verifies the `.ico`
  container.

### Notes

- Icon entries up to 48 px are stored as an uncompressed 32-bit DIB and larger ones as
  PNG. Windows reads PNG entries at any size from Vista onwards, but plenty of other
  code does not - the .NET Framework's `System.Drawing.Icon` throws on one - and the
  small sizes are the ones every part of the shell touches.
- Every entry is read back and decoded before the tool reports success, so a broken
  icon is caught at build time rather than as a blank square on a desktop.
- `build.ps1 -AppIcon` clears `obj\Release` and `bin\Release` first. MSBuild decides
  whether to recompile from file timestamps and a changed property does not count, so
  without that the exe silently keeps the icon from the previous publish.
- `AssemblyName` is deliberately not overridden for the named build: doing so makes
  NuGet restore fail with "Ambiguous project name". The exe is renamed afterwards,
  which is enough because the wrapper reads its own file name at run time.
- SVG support is a subset aimed at logos: `viewBox`, `path`, `rect`, `circle`,
  `ellipse`, `polygon`, `polyline`, `line`, `g` with `transform`, and linear and
  radial gradients. Text, filters, clip paths and masks are not handled.
- The `.ico` files in `icons/` are not committed. A company logo is a trademark and
  not this project's to pass on under the MIT licence, so the repo ships the recipe.
  `tools/test-assets/sample-logo.svg` is original artwork used to test the renderer.

### Fixed

- SVG `transform` lists were applied in the wrong order. SVG reads them right to left
  while WPF applies a `TransformGroup` left to right, so `translate(24 40) rotate(45)`
  rotated the already-moved shape about the origin and flung it off the canvas.

## 1.1.0 - 2026-09-06

### Added

- `loading-indicator`: a spinner that runs after the window title while a page is
  loading, so a wrapped app no longer looks frozen on a slow page. Styles: `spinner`
  (the classic `| / - \`), `bar` (`[=   ]` bouncing, DOS file manager style), `dots`,
  `braille`, and `off`. One frame every 120 ms.
- The same animation now shows in the middle of the window as `Loading  /` until the
  first page has something to draw. A freshly created WebView2 paints plain white,
  which reads as a broken app rather than a busy one.
- The tray tooltip says `<title> - loading...` while busy.

### Notes

- The spinner covers browser start-up and the first page load with no gap between
  them, and stops after 60 seconds so a single page app that never reports
  "navigation completed" cannot leave the title spinning for ever.
- The page is revealed on `DOMContentLoaded`, on `NavigationCompleted`, or after a
  15 second fallback, whichever happens first, so the placeholder can never get stuck
  in front of a working page.
- Anything that reads the window title must allow for the spinner suffix; the title is
  `"<title>"` when idle and `"<title>  <frame>"` while loading.

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
