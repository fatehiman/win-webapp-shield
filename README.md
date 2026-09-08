# win-webapp-shield

Turn any web page into a real Windows desktop app — one small `.exe` plus one `.conf`
file, no Electron, no Node, no install.

The wrapper uses **WebView2**, the Microsoft Edge engine that is already part of
Windows 11 and of up-to-date Windows 10. That is why the app stays small and starts
fast, and why it can give its memory back to Windows while you are not looking at it.

```
mstodo.exe        <- rename webappshield.exe to anything you want
mstodo.conf       <- settings, plain JSON or encrypted
mstodo.session    <- written by the app: last window position (optional)
```

---

## What it does

| | |
|---|---|
| **One file** | Self-contained single `.exe`. No .NET runtime needed. |
| **Config next to the exe** | `mstodo.exe` reads `mstodo.conf`. Missing file → error box, then exit. |
| **Encrypted config** | Optional AES-256-GCM. `encode.exe` / `decode.exe` are included. |
| **One copy per exe file** | The same `.exe` cannot run twice. Two copies in two folders can. |
| **Any window style** | Full title bar, only some buttons, or no title bar at all. |
| **Tray icon** | Close to tray, Open / Open in browser / Exit menu. |
| **Monitor & position** | Pick a monitor, centre it, or remember where the user left it. |
| **Starts hidden** | `"window-state": "tray"` never draws the window, and still loads the page. |
| **Gets out of the way** | `tray-after-load`, `min-after-30`: open, load, then leave. |
| **Frees memory** | Drops the whole browser after the window has been out of sight for a while. |
| **Real app icon** | Build the wrapped app's own logo into the exe, the window and the tray. |
| **Loading spinner** | `| / - \` after the title while a page loads, DOS style. |
| **Size while resizing** | `MS To Do - 450x320` in the title, so you can read off a size. |

---

## Quick start

1. Download the release, or build it yourself (see [docs/BUILD.md](docs/BUILD.md)).
2. Make a folder, for example `C:\apps\mstodo`.
3. Copy `webappshield.exe` into it and **rename it** to `mstodo.exe`.
4. Copy `samples/mstodo.conf` next to it, keeping the same base name.
5. Edit the `url`, `title` and `icon` lines.
6. Run `mstodo.exe`.

The name is the only link between the two files. Call the exe `jira.exe` and it reads
`jira.conf`. That is also how you run several wrapped apps from one build.

### If the app says WebView2 is missing

Windows 11 always has it. On an older Windows 10, run `setup.exe`; it downloads and
installs the runtime from Microsoft. You need to be online.

If you cannot install anything, the error box lets you point at a folder that holds a
*fixed version* WebView2 runtime (a folder with `msedgewebview2.exe` in it). The path
is remembered in the `.session` file.

---

## The config file

`mstodo.conf` is JSON with comments allowed:

```jsonc
{
  "url": "https://to-do.office.com/tasks/",
  "width": 1100,
  "height": 820,
  "monitor": 0,
  "window-type": "min+max+close+tray",
  "position": "center",
  "window-state": "normal",
  "icon": "app.ico",
  "systray": true,
  "title": "Microsoft To Do",
  "tray-open-in-browser": true,
  "sleep-after": "auto"
}
```

[`samples/mstodo.conf`](samples/mstodo.conf) is the same file with **every** setting
written out, every alternative value listed as a comment, and help text for each one.
Start from that file.

Full reference: **[docs/CONFIG.md](docs/CONFIG.md)**.

### `window-type` in one minute

| value | what you get |
|---|---|
| `""` | No title bar at all. Resizable by the edges, but not draggable. |
| `"min+max+close"` | A normal Windows window. |
| `"min+close"` | No Maximize button. |
| `"min+max+close+tray"` | Normal window **plus** a tray icon; the X hides to the tray. |
| `"min+tray"` | No working X. Quit from the tray menu. |

The tags are `min`, `max`, `close`, `tray`, joined by `+` in any order. Windows always
draws the buttons in its own standard order and style — this is a real native title
bar, not a drawn copy.

### How the X button behaves

- Tray **off** → X quits the app.
- Tray **on** → X hides the window to the tray, like most tray apps. Quit from the
  tray menu → *Exit*.
- Force it either way with `"close-action": "tray"` or `"exit"`.

---

## Giving it the right icon

A wrapper showing a generic icon does not feel like the app it wraps. Two commands:

```powershell
.\tools\make-app-icon.ps1 -Preset todo -Out .\icons\mstodo.ico
.\build.ps1 -AppName mstodo -AppIcon .\icons\mstodo.ico
```

You get `dist\mstodo.exe` with the logo compiled in, so Explorer, the taskbar and
Alt+Tab show it, plus `dist\mstodo.ico` and a `dist\mstodo.conf` already pointing at
it, so the window title bar and the tray show it too.

`make-app-icon.ps1` turns an SVG, PNG or existing icon into a proper multi-size `.ico`
(16 to 256 px, small sizes as DIB and large ones as PNG, every entry decoded and
verified before it reports success). `-ListPresets` lists the Microsoft 365 logos that
Microsoft publishes on its own brand-icon CDN:

```
todo  outlook  teams  onenote  excel  word  powerpoint  onedrive
sharepoint  access  visio  project  forms  sway  stream  delve  yammer  loop
```

Anything else: `-Source https://example.com/logo.svg`, or a local file. Prefer an SVG —
a site's favicon is usually 32 px and looks soft the moment Windows wants 256.

The logos themselves are **not** committed to this repo, because a logo is a trademark
and not mine to pass on under the MIT licence. The recipe is committed; you run it.

Full details: **[docs/ICONS.md](docs/ICONS.md)**.

---

## Telling the user it is busy

A wrapped web app has no address bar and no browser throbber, so a slow page just
looks frozen. `loading-indicator` fixes that with the oldest trick there is:

```
Microsoft To Do  |        Microsoft To Do  /
Microsoft To Do  -        Microsoft To Do  ```

One frame every 120 ms after the window title. The same animation appears in the
middle of the window (`Loading  /`) until the first page has something to draw, and
the tray tooltip says `Microsoft To Do - loading...` while it is busy.

Styles: `spinner` (the ASCII default), `bar` (`[=   ]` bouncing, DOS file manager
style), `dots`, `braille`, or `off`. It gives up after 60 seconds so a single page app
that never says "finished" cannot leave the title spinning for ever.

### Finding a size you like

Drag the window edges and the title shows what you are doing:

```
MS To Do - 450x320
```

One second after you stop, it goes back to plain. The numbers are the whole window,
which is exactly what `width` and `height` mean in the config, so you can read a size
off the title bar and paste it straight in. Turn it off with
`"show-size-on-resize": false`.

---

## Encrypting the config

```powershell
encode.exe "mstodo.conf" "Kopolop0_90u"      # in place, keeps mstodo.conf.bak
decode.exe "mstodo.conf" "Kopolop0_90u"      # back to readable JSON
encode.exe "mstodo.conf" "Kopolop0_90u" "out.conf"   # write somewhere else
```

The app reads plain and encrypted files. It checks the first bytes of the file and
decrypts only when it has to, using the key built into the exe.

**Cipher:** AES-256-GCM, key from the password with PBKDF2-HMAC-SHA256, 310 000
iterations, 16-byte random salt, 12-byte random nonce. GCM is authenticated, so a
wrong password or a damaged file is reported instead of producing rubbish.

> **What this is for.** The key `Kopolop0_90u` is compiled into the exe, so this stops
> a normal user from reading or editing the settings. It is **not** protection against
> someone who can inspect the exe. Never put a real secret in a `.conf` file.

Format details are in [docs/CRYPTO.md](docs/CRYPTO.md).

---

## Starting out of the way

`"window-state": "tray"` never draws a window — and, since it loads the page in the
background, the app is ready the moment you open it.

That readiness is not free. Measured with a simple page:

| | total memory |
|---|---|
| `"preload": true` (default) | about 366 MB, the wrapper plus 6 browser processes |
| `"preload": false` | about 47 MB, the wrapper on its own |

`sleep-after` hands it back once the window has been out of sight long enough.

If you would rather have the page loaded *and* the app quiet afterwards, let the window
open, do its work, and leave:

| `window-state` | what happens |
|---|---|
| `"tray-after-load"` | opens normally, goes to the tray once the page has loaded |
| `"min-after-load"` | opens normally, minimizes once the page has loaded |
| `"tray-after-10"` | opens normally, goes to the tray 10 seconds later |
| `"min-after-30"` | opens normally, minimizes 30 seconds later |

Touching the window first cancels it for good — a click, a key, a scroll, or moving or
resizing it. The timer is there to get the window out of your way, not to take it while
you are reading.

---

## Memory

An embedded browser is the expensive part of a wrapper like this. `sleep-after`
handles it:

- The clock only runs while the window is **minimized or hidden in the tray**.
  A window open on the desktop is never put to sleep, even if nobody touches it.
- When the limit is reached, the whole WebView2 process tree is shut down and the
  memory is returned to Windows.
- Open the window again and the browser starts up on the same page you were on.
- While it is asleep the tray icon shows a small gray dot in the top right corner,
  so you can tell a sleeping app from an awake one.

| value | meaning |
|---|---|
| `"auto"` | Less than 20 % free RAM → sleep after 10 min. Otherwise after 120 min. |
| `30` | Sleep after 30 minutes out of sight. |
| `"off"` | Never sleep. |

`"window-state": "tray"` starts hidden but still loads the page, so the sleep clock is
already running when the app starts. `"preload": false` goes back to not starting the
browser at all until the window is opened the first time.

---

## One instance per exe file

The lock is built from the **full path of the exe**, so:

- Starting `C:\apps\mstodo\mstodo.exe` twice → the second copy shows an error and exits.
- `C:\apps\a\mstodo.exe` and `C:\apps\b\mstodo.exe` → both run happily, with separate
  browser profiles and separate logins.

Set `"single-instance-action": "focus"` to make the second start quietly bring the
running window to the front instead of showing an error.

---

## What ships

| file | what it is |
|---|---|
| `webappshield.exe` | The wrapper. Rename it. ~65 MB, self-contained. |
| `encode.exe` | CLI: encrypt a file. `encode.exe "file" "password"` |
| `decode.exe` | CLI: decrypt a file. `decode.exe "file" "password"` |
| `setup.exe` | Installs the WebView2 runtime if it is missing. |
| `set-icon.exe` | Gives a built exe the icon of the `.ico` next to it. Run it with no arguments. |
| `mstodo.conf` | The fully commented sample config. |
| `app.ico` | The generic icon, drawn from code by `tools/make-icon.ps1`. |

---

## Build it yourself

```powershell
git clone https://github.com/fatehiman/win-webapp-shield.git
cd win-webapp-shield
.\build.ps1 -AppName mstodo
```

Needs the .NET SDK 8.0 or newer. Everything lands in `.\dist`.
See [docs/BUILD.md](docs/BUILD.md) for the details.

To check a build, `.\tools\smoke-test.ps1` starts the real exe with 24 different
config files and inspects the windows Windows actually created — styles, sizes, exit
codes, the session file, and the browser processes before and after a sleep.

---

## Project layout

```
src/WebAppShield/   the wrapper (WinForms + WebView2)
src/Shield.Crypto/  AES-256-GCM file format, shared by all tools
src/Cli/            source shared by encode and decode
src/Encode/         encode.exe
src/Decode/         decode.exe
src/Setup/          setup.exe, the WebView2 prerequisite installer
src/SetIcon/        set-icon.exe, changes the icon of a built exe
samples/            the fully commented mstodo.conf
docs/               CONFIG.md, CRYPTO.md, ICONS.md, BUILD.md
icons/              .ico files you build for your apps (not committed)
tools/make-icon.ps1     draws the generic app.ico, no image editor needed
tools/make-app-icon.ps1 turns an SVG, PNG or URL into a multi-size .ico
tools/smoke-test.ps1    starts the real exe and checks what Windows created
```

---

## Licence

MIT. See [LICENSE](LICENSE).
