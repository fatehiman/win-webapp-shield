# Building

## What you need

- **.NET SDK 8.0** or newer — <https://dotnet.microsoft.com/download>
- **Windows** — the wrapper is a WinForms app, so it only builds on Windows
- an internet connection the first time, to restore the WebView2 NuGet package

Nothing else. No Node, no Visual Studio, no C++ toolchain.

## Build everything

```powershell
git clone https://github.com/fatehiman/win-webapp-shield.git
cd win-webapp-shield
.\build.ps1
```

The result lands in `.\dist`:

| file | size | what it is |
|---|---|---|
| `webappshield.exe` | ~65 MB | the wrapper — rename it to whatever you like |
| `encode.exe` | ~13 MB | encrypt a `.conf` |
| `decode.exe` | ~13 MB | decrypt a `.conf` |
| `setup.exe` | ~13 MB | install the WebView2 runtime if missing |
| `set-icon.exe` | ~11 MB | give an already built exe the icon of an `.ico` next to it |
| `mstodo.conf` | | the fully commented sample config |
| `app.ico` | | a default icon |

All five are **self-contained single files**: the .NET runtime is inside them, so they
run on a machine with no .NET installed.

### Options

```powershell
.\build.ps1 -AppName mstodo     # also drop a renamed copy: dist\mstodo.exe + mstodo.conf
.\build.ps1 -Runtime win-arm64  # win-x64 (default), win-arm64, win-x86
.\build.ps1 -Clean              # wipe dist, artifacts and every bin/obj first
```

## Build one project by hand

```powershell
dotnet publish src\WebAppShield\WebAppShield.csproj -c Release -r win-x64 --self-contained true -o out
```

## Why the wrapper is 65 MB and the CLI tools are 13 MB

The wrapper carries the whole .NET desktop runtime, including WinForms, and WinForms
cannot be trimmed safely (it finds types by reflection). The CLI tools have no UI, so
they are published with `PublishTrimmed` and shrink to about a fifth of the size.

Want a 2 MB wrapper instead? Publish it framework-dependent and install the
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) on the
target machine:

```powershell
dotnet publish src\WebAppShield\WebAppShield.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true -o out-small
```

## Renaming the exe

The app finds its config from its **own file name at run time**
(`Environment.ProcessPath`), so renaming a built exe is all you need:

```
webappshield.exe  ->  mstodo.exe   reads mstodo.conf, writes mstodo.session
webappshield.exe  ->  jira.exe     reads jira.conf,   writes jira.session
```

You do not have to rebuild for each wrapped app. One build, many copies.

The single-instance lock is keyed on the full exe path, so two copies in two folders
run side by side and keep separate browser profiles.

## Project layout

```
src/WebAppShield/     the wrapper: WinForms window + WebView2 + tray + sleep logic
  Program.cs            startup: paths, config, single instance, WebView2 check
  AppConfig.cs          every setting, with defaults and validation
  ConfigLoader.cs       find, decrypt, parse the .conf
  SessionStore.cs       the .session scratch file
  MainForm.cs           the window, the tray, sleep and wake
  SleepManager.cs       when to drop the browser
  LoadingIndicator.cs   the title bar spinner shown while a page loads
  MonitorHelper.cs      monitor numbering and placement
  SingleInstance.cs     the per-exe-path lock
  Dialogs.cs            error boxes and the WebView2-missing dialog
  NativeMethods.cs      the few Win32 calls that are needed

src/Shield.Crypto/    the AES-256-GCM file format, shared by everything
src/Cli/              CliProgram.cs, compiled into both CLI tools
src/Encode/           encode.exe  (CliProgram without DECODE_MODE)
src/Decode/           decode.exe  (CliProgram with    DECODE_MODE)
src/Setup/            setup.exe, the WebView2 prerequisite installer
src/SetIcon/          set-icon.exe, the icon of a built exe, changed in place

samples/              the fully commented mstodo.conf
icons/                .ico files you build per app (not committed, see icons/README.md)

tools/make-icon.ps1      draws the generic app.ico from code
tools/make-app-icon.ps1  SVG/PNG/URL -> multi-size .ico, with presets for MS 365 logos
tools/lib/IcoWriter.ps1  writes and verifies the .ico container
tools/smoke-test.ps1     starts the real exe and checks what Windows created
build.ps1                publishes everything into dist
```

`encode.exe` and `decode.exe` are the same source file. `Decode.csproj` defines
`DECODE_MODE`, which flips the direction and the help text. That is why the two tools
can never disagree about the file format.

## Testing

```powershell
.\build.ps1
.\tools\smoke-test.ps1              # about 1 minute
.\tools\smoke-test.ps1 -IncludeSlow # about 4 minutes, adds the sleep/wake test
```

The script starts the real exe with 24 different `.conf` files and checks what Windows
actually created: window styles read back with `GetWindowLong`, whether the close
button carries `CS_NOCLOSE`, window size and position in pixels, process exit codes,
the `.session` file contents, the `WASENC1` header, every frame of the loading
spinner, the size shown while resizing, and the browser child processes before and
after a sleep. Nothing is mocked.

Some scenarios run a loopback TCP server as a stand-in web site. That answers
questions no screenshot can — whether the wrapper really fetched its URL while sitting
in the tray, for one — and lets a scenario serve its own HTML, which is how the
cancel-on-interaction check fires a real `pointerdown` inside the page without
synthesising OS input.

```
== 14. sleep-after frees the browser while the window is hidden
  [pass] the embedded browser is running (found 1)
  [pass] the browser processes were shut down
  [pass] memory dropped (56.9 MB -> 10.5 MB)
  [pass] opening it again restarted the browser (found 1)

passed: 92   failed: 0
```

Add `-KeepFiles` to keep the temporary folders (with any `app.error.log`) for a look.

## When something is hard to see from outside

Set `WWS_TRACE` to anything before starting a wrapped app and it appends to
`<exename>.trace.log` next to the exe:

```powershell
$env:WWS_TRACE = 1
.\mstodo.exe
```

```
17:33:07.739  startup plan: initial=Normal then=Tray afterSeconds= afterLoad=True
17:33:09.102  showing the window
17:33:11.240  deferred window action running: Tray
```

It records the `window-state` plan the app read, whether the deferred tray/minimize
ran, what cancelled it if it did not, when the browser was dropped for sleep, and when
the window was shown. The smoke test turns it on and prints the file when a startup
timing check fails, which is how a real bug gets told apart from someone clicking the
window mid-run.

## Continuous integration

[`.github/workflows/build.yml`](../.github/workflows/build.yml) builds everything on
`windows-latest` for every push, and uploads `dist` as a build artifact. Pushing a tag
like `v1.0.0` also creates a GitHub release with a zip attached.

The encryption round trip, the `setup.exe` check and the 86 fast smoke-test checks are
all hard gates there — the hosted Windows runner does give a real desktop session, so
the window checks work. The sleep/wake test (`-IncludeSlow`) needs minutes of real
waiting, so run that one locally before cutting a release.

## Regenerating the icon

```powershell
.\tools\make-icon.ps1
```

It draws the shield with `System.Drawing` and writes a real multi-size `.ico`
(16 / 24 / 32 / 48 / 64 / 128 / 256 px, PNG-compressed entries). `build.ps1` runs it
for you. To use your own artwork, just replace `src/WebAppShield/app.ico`.
