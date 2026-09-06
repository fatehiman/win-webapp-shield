# Configuration reference

The app reads a single file: **a file with the same name as the exe, with the `.conf`
extension, in the same folder as the exe**.

```
C:\apps\mstodo\mstodo.exe   ->   C:\apps\mstodo\mstodo.conf
```

If that file is missing, the app shows a modal error box with a Close button and
exits. It never falls back to defaults.

## File format

JSON, with two extras allowed:

- `// line comments` and `/* block comments */`
- a comma after the last item

Single quotes are **not** JSON. Always use `"double quotes"`.
Backslashes in Windows paths must be doubled: `"C:\\apps\\icons\\mstodo.ico"`.

The file may be plain text or encrypted (see [CRYPTO.md](CRYPTO.md)). The app looks at
the first bytes and decides by itself.

Every setting has a default, so a config as short as this already works:

```jsonc
{ "url": "https://example.com" }
```

If a setting has a value the app does not understand, a warning box lists the problems
and the app carries on with the defaults for those settings.

---

## Settings

### `url` — the page to open

| | |
|---|---|
| type | string |
| default | `"about:blank"` |

Must be a full address with a scheme. `https://…`, `http://…` and `file:///…` all work.

```jsonc
"url": "https://to-do.office.com/tasks/"
```

---

### `width`, `height` — startup size

| | |
|---|---|
| type | number, 100 – 30000 |
| default | `1024` × `768` |

Real device pixels. Windows display scaling does not change them, so the window is the
size you asked for on every machine.

---

### `monitor` — which screen

| | |
|---|---|
| type | number, 0 or more |
| default | `0` |

- `0` — the primary monitor
- `1` — "Display 1" in Windows Settings, `2` — "Display 2", and so on

The numbers follow the `\\.\DISPLAYn` device names, which is what Windows Settings
shows. If the numbers cannot be read, screens are ordered left to right instead.

A monitor that does not exist, or cannot be detected, silently falls back to the
primary monitor.

---

### `window-type` — title bar and buttons

| | |
|---|---|
| type | string |
| default | `"min+max+close"` |

`""` (empty) means **no title bar at all**: no caption, no buttons, no icon. The window
can still be resized by dragging its edges, but there is no caption to drag it by.

Anything else gives a **real native Windows title bar**, plus whichever buttons you
list. Tags, joined with `+` (`,`, `|` and spaces also work), in any order:

| tag | effect |
|---|---|
| `min` | show the Minimize button |
| `max` | show the Maximize button |
| `close` | the X button works |
| `tray` | put an icon in the notification area, and the X hides to it |

Notes:

- The order in the file does not matter. Windows draws the buttons in its own standard
  order, style and theme. `"close+min+max"` and `"min+max+close"` look identical.
- If `close` is **not** listed, Windows draws a real greyed-out X and blocks `Alt+F4`.
  Make sure the user has another way out — usually `tray`, then *Exit* in the tray menu.
- If `tray` **is** listed, the tray icon is on even when `"systray": false`.

Common combinations:

```jsonc
"window-type": ""                    // frameless
"window-type": "min+max+close"       // an ordinary window
"window-type": "min+close"           // no Maximize
"window-type": "min+max+close+tray"  // ordinary window + tray icon, X hides
"window-type": "min+tray"            // no working X, quit from the tray menu
```

---

### `position` — where the window appears

| | |
|---|---|
| type | string |
| default | `"center"` |

| value | meaning |
|---|---|
| `"center"` | centre of the monitor chosen by `monitor` |
| `"last"` | where the user left it last time |
| `"x,y"` | an exact spot, from the top-left of that monitor's working area |

`"last"` is stored in `<exename>.session` next to the exe. If that file cannot be
written — a read-only folder, for example — the app silently uses `"center"` instead.
A saved position that is no longer on any connected screen is also ignored.

---

### `window-state` — how it starts

| | |
|---|---|
| type | string |
| default | `"normal"` |

| value | meaning |
|---|---|
| `"normal"` | a normal window |
| `"min"` | minimized to the taskbar |
| `"max"` | maximized |
| `"tray"` | hidden in the notification area |

`"tray"` really means hidden: the window is never mapped on screen, so there is no
flash. The embedded browser is not started either — it starts the first time the user
opens the window, which is the cheapest possible startup.

`"tray"` turns the tray icon on by itself.

---

### `icon` — window, taskbar and tray icon

| | |
|---|---|
| type | string, path to an `.ico` file |
| default | `""` — the icon built into the exe |

An absolute path, or a path relative to the exe folder. A missing or broken file is
ignored and the built-in icon is used; it never stops the app.

```jsonc
"icon": "app.ico"
"icon": "icons\\mstodo.ico"
"icon": "C:\\apps\\icons\\mstodo.ico"
```

---

### `systray` — tray icon on or off

| | |
|---|---|
| type | boolean |
| default | `false` |

If any other setting needs the tray — `tray` in `window-type`, or
`"window-state": "tray"` — the tray is switched on even when this says `false`.

---

### `title` — title bar text and tray tooltip

| | |
|---|---|
| type | string |
| default | `""` — the exe file name |

---

### `tray-open-in-browser` — extra tray menu item

| | |
|---|---|
| type | boolean |
| default | `false` |

The tray right-click menu always has **Open** and **Exit**. Set this to `true` to also
get **Open in browser**, which opens the page you are currently on in the default
Windows browser.

Left-click or double-click on the tray icon always opens the window.

---

### `sleep-after` — give memory back

| | |
|---|---|
| type | `"auto"`, `"off"`, or a number of minutes |
| default | `"auto"` |

Inactivity means the window is **minimized or hidden in the tray**. A window that is
open on the desktop is never put to sleep, however long the user ignores it.

When the limit is reached the app shuts down the whole WebView2 process tree, forces a
garbage collection and trims its working set. Opening the window again starts the
browser and loads the page you were on.

| value | meaning |
|---|---|
| `"auto"` | free RAM below 20 % → 10 minutes. Otherwise 120 minutes. |
| `120` | 120 minutes out of sight |
| `"off"` (or `"never"`, or `0`) | never sleep |

In `"auto"` mode the free memory is re-checked every 30 seconds, so the limit follows
what the machine is doing.

---

## Extra settings

Every one of these has a working default. You can delete them all.

### `loading-indicator`

`"spinner"` (default), `"bar"`, `"dots"`, `"braille"`, or `"off"`.

While a page is loading, a small animation runs after the window title, the way DOS
and Linux tools have always shown "still working":

```
Microsoft To Do  |
Microsoft To Do  /
Microsoft To Do  -
Microsoft To Do  ```

| value | frames |
|---|---|
| `"spinner"` | `|` `/` `-` `\` — plain ASCII, works in every font |
| `"bar"` | `[=   ]` `[ =  ]` `[  = ]` `[   =]` — a block bouncing in brackets |
| `"dots"` | `.` `..` `...` |
| `"braille"` | `⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏` — smooth, needs a font with Braille characters |
| `"off"` | the title never changes |

It runs in three places at once:

- after the window title, one frame every 120 ms
- in the middle of the window, as `Loading  /`, until the first page has something to
  draw — a fresh WebView2 paints plain white, which looks broken rather than busy
- in the tray tooltip, which just says `<title> - loading...` while busy

The animation starts while the browser is starting up and keeps running through the
first page load, with no gap in between. It stops on its own after 60 seconds, so a
single page app that never reports "finished" cannot leave the title spinning for ever.

### `close-action`

`"auto"` (default), `"tray"`, `"exit"`.

What the X button does:

- `"auto"` — tray on → hide to tray; tray off → quit
- `"tray"` — always hide to the tray
- `"exit"` — always quit, even with the tray on

### `resizable`

`true` (default) / `false`. Can the user drag the edges to resize?

### `always-on-top`

`false` (default) / `true`. Keep the window above every other window.

### `minimize-to-tray`

`true` (default) / `false`. Only matters when the tray is on. `false` makes the
Minimize button go to the taskbar as usual, while the X still hides to the tray.

Note: with `true`, `"window-state": "min"` ends up in the tray as well, because the
window starts minimized and is then hidden. Set this to `false` if you want a taskbar
button at start-up.

### `remember-size`

`true` (default) / `false`. With `"position": "last"`, also restore the size and the
maximized state the user left behind.

### `zoom`

Number between `0.25` and `5.0`. Default `1.0` (100 %).

### `user-agent`

String. `""` (default) keeps the Edge user agent, which is what sites expect. Only
change it if a site refuses to load.

### `single-instance-action`

`"error"` (default) or `"focus"`.

What happens when the user starts the **same exe file** a second time:

- `"error"` — a message box, then the new copy exits
- `"focus"` — the new copy quietly tells the running one to come to the front, then exits

Two copies of the exe in two different folders are two different applications and can
always run at the same time. This setting is only about starting one exact file twice.

### `external-links-in-browser`

`false` (default) / `true`. When `true`, a link the user clicks that leads to a
different host than the start `url` opens in the default Windows browser instead of
inside the window. Pop-up windows follow the same rule.

When `false`, pop-ups are loaded in the same window, so the app never grows extra
windows.

### `context-menu`

`true` (default) / `false`. The web page's right-click menu (Copy, Paste, Reload …).

### `dev-tools`

`false` (default) / `true`. F12 developer tools. Keep it off for normal users.

### `data-dir`

Where the browser keeps cookies, logins and cache.

`""` (default) means
`%LOCALAPPDATA%\WinWebAppShield\<exename>-<hash of the exe path>`. Because the hash
comes from the exe path, two copies in two folders keep separate logins.

A relative path is resolved from the exe folder, which is what you want for a portable
install on a USB stick:

```jsonc
"data-dir": "browser-data"
```

If the folder cannot be created, the app falls back to a folder under `%TEMP%`.

### `webview2-path`

Folder of a *fixed version* WebView2 runtime that you ship yourself. It must contain
`msedgewebview2.exe`. `""` (default) uses the runtime installed in Windows.

### `webview2-args`

Extra command-line switches passed to the embedded browser. Leave `""` unless you know
you need one.

---

## The `.session` file

The app writes `<exename>.session` next to the exe. It is plain JSON, never encrypted,
and never required:

```json
{
  "window": { "x": 410, "y": 130, "width": 1100, "height": 820, "state": "normal" },
  "lastUrl": "https://to-do.office.com/tasks/todosdue",
  "webview2Path": "",
  "savedAt": "2026-09-06 11:04:22"
}
```

| field | used for |
|---|---|
| `window` | `"position": "last"` and `remember-size` |
| `lastUrl` | the page to restore after a sleep or a restart |
| `webview2Path` | a runtime folder the user picked in the error dialog |

Delete it any time; the app makes a new one. If the folder is read-only, every write is
skipped in silence and the app runs normally.
