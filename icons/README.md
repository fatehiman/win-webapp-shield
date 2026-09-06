# icons/

Working folder for the `.ico` files you build your wrapped apps with.

**The `.ico` files here are not committed.** They are other companies' logos, and a
logo is a trademark: it is not mine to hand on under this project's MIT licence. Using
one for a wrapper of that company's own web app, on your own machine, is ordinary and
fine — redistributing it in a public repository is a different thing. So the repo ships
the recipe, not the artwork.

Rebuild whatever you need in one command:

```powershell
.\tools\make-app-icon.ps1 -Preset todo -Out .\icons\mstodo.ico
```

`-ListPresets` shows the Microsoft 365 logos that Microsoft publishes on its own Fluent
brand-icon CDN:

```
todo  outlook  teams  onenote  excel  word  powerpoint  onedrive
sharepoint  access  visio  project  forms  sway  stream  delve  yammer  loop
```

For anything else, point `-Source` at a file or a URL:

```powershell
.\tools\make-app-icon.ps1 -Source https://example.com/logo.svg -Out .\icons\example.ico
.\tools\make-app-icon.ps1 -Source .\my-logo.png -Out .\icons\myapp.ico -Padding 0.08
```

An SVG source is worth hunting for. A site's `favicon.ico` is usually 16 to 32 pixels,
which looks soft the moment Windows wants 48 or 256 for Explorer, while an SVG renders
sharply at every size.

Then build an exe that carries it:

```powershell
.\build.ps1 -AppName mstodo -AppIcon .\icons\mstodo.ico
```

See [docs/ICONS.md](../docs/ICONS.md) for the whole story.
