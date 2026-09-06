# Giving a wrapped app its own icon

A wrapper that still shows the generic shield does not feel like the app it wraps.
There are **two** icons to think about, and they come from different places:

| what you see | where it comes from | how to set it |
|---|---|---|
| the exe in Explorer, the taskbar, Alt+Tab, pinned shortcuts | stored in the exe as a Win32 resource | `build.ps1 -AppIcon`, or `set-icon.exe` on an exe you already have |
| the window title bar, the tray icon, the tray tooltip | the `.ico` file named by `icon` in the `.conf`, read at run time | `"icon": "mstodo.ico"` |

Setting only the first leaves the window showing the generic icon. Setting only the
second leaves Explorer showing it. `build.ps1 -AppIcon` does both: it compiles the icon
in, copies the `.ico` next to the exe, and points the generated `.conf` at it.

---

## The short version

```powershell
.\tools\make-app-icon.ps1 -Preset todo -Out .\icons\mstodo.ico
.\build.ps1 -AppName mstodo -AppIcon .\icons\mstodo.ico
```

That gives you `dist\mstodo.exe`, `dist\mstodo.ico` and a `dist\mstodo.conf` whose
`icon` line already points at it.

---

## Without a rebuild: set-icon.exe

`build.ps1 -AppIcon` needs the source and the .NET SDK. When you already have a built
`webappshield.exe` — from a release, say — `set-icon.exe` does the same to it:

```
C:pps\mstodo    mstodo.exe        <- webappshield.exe, renamed
    mstodo.ico        <- made by make-app-icon.ps1, or any .ico
    mstodo.conf
    set-icon.exe
```

Double click `set-icon.exe`, or run it from a prompt:

```
Icon : C:pps\mstodo\mstodo.ico
Exe  : C:pps\mstodo\mstodo.exe
       16x16 32-bit BMP
       ...
       256x256 32-bit PNG
Done. mstodo.exe now carries mstodo.ico.
```

With no arguments it takes the **first `.ico` in its own folder**, sorted by name, and
the exe with the **same base name**. Either can be named instead:

```powershell
.\set-icon.exe mstodo.ico              # that icon, mstodo.exe
.\set-icon.exe brand.ico mstodo.exe    # both named
```

Set the `icon` line in the `.conf` as well, or the window and the tray keep the old
one:

```json
"icon": "mstodo.ico",
```

A few things worth knowing:

- **Explorer caches icons.** The new icon shows up at once in a fresh folder window,
  but a pinned shortcut or a stale thumbnail can lag. The taskbar and Alt+Tab pick it
  up the next time the app starts.
- **The app must not be running**, and the exe must not be open in another program.
  Windows refuses to edit a file that is in use, and `set-icon` says so.
- **A code signed exe is refused.** Rewriting it would break the signature anyway.
- Every icon already inside the exe is replaced, not added to.
- A copy of the exe is kept as `<name>.exe.set-icon-backup` while the work is going on
  and removed once it has gone through. If you ever see one left behind, the original
  is in it.

### Why this is not just a resource edit

A `.NET` single file exe is a small native program with the whole app appended after
the end of the last PE section, and the offsets inside that block count from the start
of the file. Windows rewrites the program when it stores a resource, which moves that
end. Ordinary resource editors leave the app pointing at nothing:

```
Failure processing application bundle; possible file corruption.
```

`set-icon` takes the block off, lets Windows write the icon, appends the block again
and shifts every offset in it — the marker in the native part, the deps.json and
runtimeconfig.json locations, and one per file inside the app. That is why it works on
the wrapper, and why it stops rather than guesses when the appended data is not a
bundle it recognises.

---

## Finding a good source image

Start from an **SVG** whenever you can. Windows asks for the icon at 16, 20, 24, 32,
40, 48, 64, 128 and 256 pixels; a vector draws sharply at all of them, while a 32 pixel
favicon blown up to 256 looks like a smudge.

Three places worth trying, best first:

1. **The company's own brand-icon CDN.** Microsoft publishes its product logos as SVG,
   and `-Preset` is a shortcut for them:

   ```powershell
   .\tools\make-app-icon.ps1 -ListPresets
   ```

   ```
   todo  outlook  teams  onenote  excel  word  powerpoint  onedrive
   sharepoint  access  visio  project  forms  sway  stream  delve  yammer  loop
   ```

2. **The site's web app manifest**, usually `/manifest.json` or `/site.webmanifest`.
   Progressive web apps list 192 and 512 pixel PNGs there, which are big enough.

3. **The favicon**, `https://the-site/favicon.ico`, as a last resort. Check what you
   actually got: many sites now serve a whole company's icon rather than the product's.
   `to-do.office.com/favicon.ico`, for instance, hands back the **Outlook** icon,
   because To Do's web app moved in under Outlook.

Then:

```powershell
.\tools\make-app-icon.ps1 -Source https://example.com/logo.svg -Out .\icons\example.ico
.\tools\make-app-icon.ps1 -Source .\logo.png              -Out .\icons\example.ico
.\tools\make-app-icon.ps1 -Source .\existing.ico          -Out .\icons\example.ico
```

The tool warns you when a raster source is smaller than the largest size it has to
produce, so a soft icon never comes as a surprise.

### Options

| option | what it does |
|---|---|
| `-Preset <name>` | a Microsoft 365 logo, instead of `-Source` |
| `-ListPresets` | print the preset names |
| `-Sizes 16,32,48,256` | choose which sizes go in the file |
| `-Padding 0.08` | leave 8 % of the canvas empty around the artwork |
| `-Background '#2564CF'` | fill the transparent area with a flat colour |

`-Padding` is for logos drawn edge to edge, which look cramped next to normal app
icons. `-Background` is for a logo that is a plain dark shape and would disappear on a
dark taskbar.

---

## What the tool produces

A real multi-size `.ico`, with the storage format chosen per size the way icon editors
choose it:

```
   16x16  DIB     1128 bytes  ok
   20x20  DIB     1720 bytes  ok
   24x24  DIB     2440 bytes  ok
   32x32  DIB     4264 bytes  ok
   40x40  DIB     6760 bytes  ok
   48x48  DIB     9640 bytes  ok
   64x64  PNG     1349 bytes  ok
  128x128 PNG     2753 bytes  ok
  256x256 PNG     5343 bytes  ok
```

Sizes up to 48 are stored as an uncompressed 32-bit DIB, larger ones as PNG. Windows
reads PNG entries at any size from Vista onwards, but plenty of other code does not —
the .NET Framework's `System.Drawing.Icon` throws on one. The small sizes are the ones
everything touches, so they are stored the way every reader understands, and only the
big sizes get the compression they actually need.

Every entry is then read back and decoded before the tool reports success, so a broken
icon is caught here instead of quietly turning into a blank square on someone's
desktop.

### How much SVG it understands

Enough for logos, not more: `viewBox`, `path`, `rect`, `circle`, `ellipse`, `polygon`,
`polyline`, `line`, `g` with `transform`, and linear and radial gradients. Paths go
through WPF's geometry parser, whose mini-language is a superset of the SVG path
syntax, so arcs and curves are handled properly.

Text, filters, clip paths, masks and embedded images are **not** handled. If a logo
uses them, open it in a vector editor, export a 512 or 1024 pixel PNG, and pass that
instead.

---

## Trademarks

The `.ico` files in `icons/` are **not** committed to this repository, and the
`.gitignore` keeps them out.

A company's logo is its trademark. Using one as the icon of a wrapper around that
company's own web app, on your own machine, is ordinary and fine. Redistributing the
artwork inside a public MIT-licensed repository is a different thing, and not something
this project has the right to do. So the repo ships the recipe and you run it — which
also means you always get the current version of the logo rather than a stale copy.

The one icon that **is** committed is `src/WebAppShield/app.ico`, the generic shield,
drawn from code by `tools/make-icon.ps1`.
