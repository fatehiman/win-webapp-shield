<#
.SYNOPSIS
    Starts the built wrapper with different .conf files and checks that it behaves.

.DESCRIPTION
    Every check looks at the real window that Windows created: its styles, whether it
    is visible, the process exit code, and the files left behind. Nothing is mocked.

    Run .\build.ps1 first, or pass -Exe with a path to the wrapper.

.EXAMPLE
    .\tools\smoke-test.ps1
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [string]$WorkRoot,
    [switch]$KeepFiles,
    [switch]$IncludeSlow
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $root 'dist\webappshield.exe' }
if (-not (Test-Path $Exe)) { throw "Wrapper not found: $Exe`nRun .\build.ps1 first." }
if (-not $WorkRoot) { $WorkRoot = Join-Path $env:TEMP ("wws-smoke-" + [guid]::NewGuid().ToString('N').Substring(0, 8)) }

# --- Win32 helpers ---------------------------------------------------------
if (-not ('WwsWin' -as [type])) {
    Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class WwsWin
{
    public class Info
    {
        public IntPtr Handle;
        public string ClassName = "";
        public string Title = "";
        public bool Visible;
        public int Style;
        public int ClassStyle;
        public int X, Y, Width, Height;

        public bool HasCaption   { get { return (Style & 0x00C00000) == 0x00C00000; } }
        public bool HasMinButton { get { return (Style & 0x00020000) != 0; } }
        public bool HasMaxButton { get { return (Style & 0x00010000) != 0; } }
        public bool HasThickFrame{ get { return (Style & 0x00040000) != 0; } }
        public bool CloseBlocked { get { return (ClassStyle & 0x0200) != 0; } }
        public bool IsDialog     { get { return ClassName == "#32770"; } }
    }

    delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongW")] static extern uint GetClassLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    static int ClassStyleOf(IntPtr hwnd)
    {
        const int GCL_STYLE = -26;
        try
        {
            if (IntPtr.Size == 8) return (int)GetClassLongPtr64(hwnd, GCL_STYLE).ToInt64();
            return (int)GetClassLong32(hwnd, GCL_STYLE);
        }
        catch { return 0; }
    }

    public static Info[] TopLevel(int processId)
    {
        var found = new List<Info>();
        EnumWindows(delegate(IntPtr hwnd, IntPtr param)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid != (uint)processId) return true;

            var cls = new StringBuilder(256); GetClassNameW(hwnd, cls, 256);
            var txt = new StringBuilder(512); GetWindowTextW(hwnd, txt, 512);
            RECT r; GetWindowRect(hwnd, out r);

            found.Add(new Info {
                Handle = hwnd,
                ClassName = cls.ToString(),
                Title = txt.ToString(),
                Visible = IsWindowVisible(hwnd),
                Style = GetWindowLong(hwnd, -16),
                ClassStyle = ClassStyleOf(hwnd),
                X = r.Left, Y = r.Top, Width = r.Right - r.Left, Height = r.Bottom - r.Top
            });
            return true;
        }, IntPtr.Zero);
        return found.ToArray();
    }

    public static void Close(IntPtr hwnd) { PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); }

    /// <summary>WM_SYSCOMMAND, e.g. 0xF020 = minimize.</summary>
    public static void SysCommand(IntPtr hwnd, int command)
    {
        PostMessage(hwnd, 0x0112, new IntPtr(command), IntPtr.Zero);
    }
}
'@
}

# --- tiny test harness -----------------------------------------------------
$script:Passed = 0
$script:Failed = 0
$script:Failures = @()

function Show-Windows([System.Diagnostics.Process]$proc) {
    foreach ($w in [WwsWin]::TopLevel($proc.Id)) {
        Write-Host ("         hwnd={0} class={1} visible={2} {3}x{4} title='{5}'" -f `
            $w.Handle, $w.ClassName, $w.Visible, $w.Width, $w.Height, $w.Title) -ForegroundColor DarkGray
    }
}

function Check([string]$name, [bool]$condition, [string]$detail = '') {
    if ($condition) {
        Write-Host "  [pass] $name" -ForegroundColor Green
        $script:Passed++
    }
    else {
        Write-Host "  [FAIL] $name $detail" -ForegroundColor Red
        $script:Failed++
        $script:Failures += $name
    }
}

function Scenario([string]$name) {
    Write-Host ''
    Write-Host "== $name" -ForegroundColor Cyan
}

function New-Case([string]$name, [string]$conf, [string]$appName = 'app') {
    $dir = Join-Path $WorkRoot $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item $Exe (Join-Path $dir "$appName.exe") -Force
    if ($null -ne $conf) {
        Set-Content -Path (Join-Path $dir "$appName.conf") -Value $conf -Encoding utf8
    }
    return $dir
}

function Start-App([string]$dir, [string]$appName = 'app') {
    return Start-Process (Join-Path $dir "$appName.exe") -PassThru
}

# Process.Kill($true) only exists on .NET Core, and Windows PowerShell 5.1 runs on
# .NET Framework, so use Stop-Process and take the browser children down too.
function Kill-App([System.Diagnostics.Process]$proc) {
    if ($null -eq $proc) { return }
    try {
        $id = $proc.Id
        Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.ParentProcessId -eq $id } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    }
    catch { }
}

function Wait-Window([System.Diagnostics.Process]$proc, [int]$timeoutSeconds = 25) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($proc.HasExited) { return @() }
        $windows = @([WwsWin]::TopLevel($proc.Id) | Where-Object { $_.Visible })
        if ($windows.Count -gt 0) { return $windows }
        Start-Sleep -Milliseconds 300
    }
    return @()
}

function Stop-App([System.Diagnostics.Process]$proc, [int]$timeoutSeconds = 10) {
    if ($proc.HasExited) { return $proc.ExitCode }
    foreach ($w in [WwsWin]::TopLevel($proc.Id)) { [WwsWin]::Close($w.Handle) }
    if ($proc.WaitForExit($timeoutSeconds * 1000)) { return $proc.ExitCode }
    Kill-App $proc
    return $null
}

function Close-Dialogs([System.Diagnostics.Process]$proc, [int]$timeoutSeconds = 20) {
    # Waits for a message box, closes it, and returns the exit code of the process.
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $sawDialog = $false
    while ((Get-Date) -lt $deadline) {
        if ($proc.HasExited) { break }
        $dialogs = @([WwsWin]::TopLevel($proc.Id) | Where-Object { $_.IsDialog -and $_.Visible })
        foreach ($d in $dialogs) { $sawDialog = $true; [WwsWin]::Close($d.Handle) }
        Start-Sleep -Milliseconds 300
    }
    if (-not $proc.WaitForExit(8000)) { Kill-App $proc; return @{ Dialog = $sawDialog; ExitCode = $null } }
    return @{ Dialog = $sawDialog; ExitCode = $proc.ExitCode }
}

$Fast = 'https://example.com/'

Write-Host "Wrapper : $Exe"
Write-Host "Workdir : $WorkRoot"
New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null

try {
    # ---------------------------------------------------------------- 1
    Scenario '1. Missing .conf file shows a modal error and exits'
    $dir = New-Case 'missing-conf' $null
    $p = Start-App $dir
    $r = Close-Dialogs $p
    Check 'a modal dialog was shown' $r.Dialog
    Check 'exit code is 2 (configuration error)' ($r.ExitCode -eq 2) "got '$($r.ExitCode)'"

    # ---------------------------------------------------------------- 2
    Scenario '2. Broken JSON is reported, not ignored'
    $dir = New-Case 'bad-json' '{ "url": "https://example.com", '
    $p = Start-App $dir
    $r = Close-Dialogs $p
    Check 'a modal dialog was shown' $r.Dialog
    Check 'exit code is 2' ($r.ExitCode -eq 2) "got '$($r.ExitCode)'"

    # ---------------------------------------------------------------- 3
    Scenario '3. A normal window: size, title and all three buttons'
    $conf = @"
{
  // comments and a trailing comma must both be accepted
  "url": "$Fast",
  "width": 820,
  "height": 560,
  "window-type": "min+max+close",
  "position": "center",
  "title": "Smoke Test Window",
  "sleep-after": "off",
}
"@
    $dir = New-Case 'normal' $conf
    $p = Start-App $dir
    $windows = Wait-Window $p
    $w = $windows | Where-Object { $_.Title -eq 'Smoke Test Window' } | Select-Object -First 1
    Check 'the window exists with the configured title' ($null -ne $w)
    if ($w) {
        Check 'it has a native title bar' $w.HasCaption
        Check 'minimize button is present' $w.HasMinButton
        Check 'maximize button is present' $w.HasMaxButton
        Check 'close button is enabled' (-not $w.CloseBlocked)
        Check 'it is resizable' $w.HasThickFrame
        Check "width is 820 (got $($w.Width))" ($w.Width -eq 820)
        Check "height is 560 (got $($w.Height))" ($w.Height -eq 560)
    }
    $code = Stop-App $p
    Check 'closing the window exits the process' ($code -eq 0) "got '$code'"

    # ---------------------------------------------------------------- 4
    Scenario '4. window-type "min" hides max and disables close'
    $conf = "{ `"url`": `"$Fast`", `"window-type`": `"min`", `"title`": `"OnlyMin`", `"sleep-after`": `"off`" }"
    $dir = New-Case 'only-min' $conf
    $p = Start-App $dir
    $w = (Wait-Window $p) | Where-Object { $_.Title -eq 'OnlyMin' } | Select-Object -First 1
    Check 'the window exists' ($null -ne $w)
    if ($w) {
        Check 'it still has a title bar' $w.HasCaption
        Check 'minimize button is present' $w.HasMinButton
        Check 'maximize button is gone' (-not $w.HasMaxButton)
        Check 'close button is greyed out by Windows (CS_NOCLOSE)' $w.CloseBlocked
    }
    $null = Stop-App $p
    Kill-App $p

    # ---------------------------------------------------------------- 5
    Scenario '5. window-type "" gives a frameless window'
    $conf = "{ `"url`": `"$Fast`", `"window-type`": `"`", `"width`": 640, `"height`": 480, `"sleep-after`": `"off`" }"
    $dir = New-Case 'frameless' $conf
    $p = Start-App $dir
    $w = (Wait-Window $p) | Where-Object { $_.ClassName -like 'WindowsForms*' } | Select-Object -First 1
    Check 'the window exists' ($null -ne $w)
    if ($w) {
        Check 'there is no caption' (-not $w.HasCaption)
        Check "width is 640 (got $($w.Width))" ($w.Width -eq 640)
    }
    $null = Stop-App $p
    Kill-App $p

    # ---------------------------------------------------------------- 6
    Scenario '6. window-state "tray" never draws a window'
    $conf = "{ `"url`": `"$Fast`", `"window-type`": `"min+max+close+tray`", `"window-state`": `"tray`", `"systray`": true, `"title`": `"TrayOnly`" }"
    $dir = New-Case 'tray-start' $conf
    $p = Start-App $dir
    $sawWindow = $false
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        if ($p.HasExited) { break }
        if (@([WwsWin]::TopLevel($p.Id) | Where-Object { $_.Visible -and $_.Width -gt 1 }).Count -gt 0) { $sawWindow = $true; break }
        Start-Sleep -Milliseconds 200
    }
    Check 'the process is still running' (-not $p.HasExited)
    Check 'no window was drawn at any point' (-not $sawWindow)
    $browsers = @(Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ParentProcessId -eq $p.Id })
    Check 'the embedded browser was not started either' ($browsers.Count -eq 0) "found $($browsers.Count)"
    Kill-App $p

    # ---------------------------------------------------------------- 7
    Scenario '7. The same exe cannot run twice'
    $conf = "{ `"url`": `"$Fast`", `"title`": `"Single`", `"sleep-after`": `"off`" }"
    $dir = New-Case 'single-instance' $conf
    $first = Start-App $dir
    $null = Wait-Window $first
    Check 'the first copy is running' (-not $first.HasExited)

    $second = Start-App $dir
    $r = Close-Dialogs $second
    Check 'the second copy showed a modal error' $r.Dialog
    Check 'the second copy exited with code 3' ($r.ExitCode -eq 3) "got '$($r.ExitCode)'"
    Check 'the first copy is untouched' (-not $first.HasExited)

    # a copy of the same exe in another folder must be allowed
    $other = New-Case 'single-instance-other' $conf
    $third = Start-App $other
    $null = Wait-Window $third
    Check 'the same exe in a different folder runs fine' (-not $third.HasExited)
    $null = Stop-App $third
    $null = Stop-App $first

    # ---------------------------------------------------------------- 8
    Scenario '8. position "last" writes and reuses the .session file'
    $conf = "{ `"url`": `"$Fast`", `"width`": 700, `"height`": 500, `"position`": `"last`", `"title`": `"Session`", `"sleep-after`": `"off`" }"
    $dir = New-Case 'session' $conf
    $p = Start-App $dir
    $null = Wait-Window $p
    $null = Stop-App $p

    $sessionFile = Join-Path $dir 'app.session'
    Check 'app.session was written next to the exe' (Test-Path $sessionFile)
    if (Test-Path $sessionFile) {
        $session = Get-Content $sessionFile -Raw | ConvertFrom-Json
        Check 'it holds the window position' ($null -ne $session.window.x -and $null -ne $session.window.y)
        Check "it holds the size (got $($session.window.width)x$($session.window.height))" ($session.window.width -eq 700)
        Check 'it holds the last url' ($session.lastUrl -like 'https://example.com*') "got '$($session.lastUrl)'"

        # start again: the window must come back where it was
        $before = $session.window
        $p2 = Start-App $dir
        $w2 = (Wait-Window $p2) | Where-Object { $_.Title -eq 'Session' } | Select-Object -First 1
        Check 'the second start restored the saved position' ($null -ne $w2 -and $w2.X -eq $before.x -and $w2.Y -eq $before.y) "got $($w2.X),$($w2.Y) want $($before.x),$($before.y)"
        $null = Stop-App $p2
    }

    # ---------------------------------------------------------------- 9
    Scenario '9. A monitor that does not exist falls back to the primary one'
    $conf = "{ `"url`": `"$Fast`", `"monitor`": 9, `"width`": 500, `"height`": 400, `"title`": `"Monitor9`", `"sleep-after`": `"off`" }"
    $dir = New-Case 'monitor-fallback' $conf
    $p = Start-App $dir
    $w = (Wait-Window $p) | Where-Object { $_.Title -eq 'Monitor9' } | Select-Object -First 1
    Check 'the window opened anyway' ($null -ne $w)
    if ($w) {
        Add-Type -AssemblyName System.Windows.Forms
        $primary = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $onPrimary = ($w.X -ge $primary.X) -and ($w.X -lt $primary.Right) -and ($w.Y -ge $primary.Y) -and ($w.Y -lt $primary.Bottom)
        Check 'it landed on the primary monitor' $onPrimary "at $($w.X),$($w.Y)"
    }
    $null = Stop-App $p

    # ---------------------------------------------------------------- 10
    Scenario '10. An encrypted .conf is read the same as a plain one'
    $encode = Join-Path (Split-Path -Parent $Exe) 'encode.exe'
    if (-not (Test-Path $encode)) {
        Write-Host '  [skip] encode.exe was not found next to the wrapper' -ForegroundColor Yellow
    }
    else {
        $conf = "{ `"url`": `"$Fast`", `"width`": 660, `"height`": 440, `"title`": `"Encrypted`", `"sleep-after`": `"off`" }"
        $dir = New-Case 'encrypted' $conf
        & $encode (Join-Path $dir 'app.conf') 'Kopolop0_90u' | Out-Null
        Check 'encode.exe succeeded' ($LASTEXITCODE -eq 0)

        $bytes = [System.IO.File]::ReadAllBytes((Join-Path $dir 'app.conf'))
        Check 'the file now starts with the WASENC1 magic' ([System.Text.Encoding]::ASCII.GetString($bytes, 0, 7) -eq 'WASENC1')

        $p = Start-App $dir
        $w = (Wait-Window $p) | Where-Object { $_.Title -eq 'Encrypted' } | Select-Object -First 1
        Check 'the app decrypted it and opened the window' ($null -ne $w)
        if ($w) { Check "width came from the encrypted file (got $($w.Width))" ($w.Width -eq 660) }
        $null = Stop-App $p
    }

    # ---------------------------------------------------------------- 11
    Scenario '11. An unknown setting value warns but does not stop the app'
    $conf = "{ `"url`": `"$Fast`", `"window-state`": `"banana`", `"title`": `"Warned`", `"sleep-after`": `"off`" }"
    $dir = New-Case 'bad-value' $conf
    $p = Start-App $dir
    # Close the warning box and keep asking until it is really gone, then the window
    # must still appear.
    $deadline = (Get-Date).AddSeconds(20)
    $sawWarning = $false
    while ((Get-Date) -lt $deadline) {
        if ($p.HasExited) { break }
        $dialogs = @([WwsWin]::TopLevel($p.Id) | Where-Object { $_.IsDialog -and $_.Visible })
        if ($dialogs.Count -eq 0) {
            if ($sawWarning) { break }
        }
        else {
            $sawWarning = $true
            foreach ($d in $dialogs) { [WwsWin]::Close($d.Handle) }
        }
        Start-Sleep -Milliseconds 250
    }
    Check 'a warning box was shown' $sawWarning
    $w = (Wait-Window $p) | Where-Object { $_.Title -eq 'Warned' } | Select-Object -First 1
    Check 'the app carried on and opened the window' ($null -ne $w)
    $null = Stop-App $p

    # ---------------------------------------------------------------- 12
    Scenario '12. With the tray on, Close hides instead of quitting'
    $conf = "{ `"url`": `"$Fast`", `"window-type`": `"min+max+close+tray`", `"systray`": true, `"title`": `"TrayClose`", `"single-instance-action`": `"focus`", `"sleep-after`": `"off`" }"
    $dir = New-Case 'tray-close' $conf
    $p = Start-App $dir
    $w = (Wait-Window $p) | Where-Object { $_.Title -eq 'TrayClose' } | Select-Object -First 1
    Check 'the window opened' ($null -ne $w)
    if ($w) {
        [WwsWin]::SysCommand($w.Handle, 0xF060)   # SC_CLOSE, exactly what clicking the X sends
        Start-Sleep -Seconds 3
        Check 'the process is still running after Close' (-not $p.HasExited)
        $stillVisible = @([WwsWin]::TopLevel($p.Id) | Where-Object { $_.Visible -and $_.Title -eq 'TrayClose' })
        Check 'the window is hidden, not shown' ($stillVisible.Count -eq 0) "found $($stillVisible.Count)"
        if ($stillVisible.Count -ne 0) { Show-Windows $p }

        # single-instance-action "focus": a second start must bring it back
        $second = Start-App $dir
        $null = $second.WaitForExit(15000)
        Check 'the second copy exited quietly with code 0' ($second.ExitCode -eq 0) "got '$($second.ExitCode)'"
        $back = $null
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            $back = @([WwsWin]::TopLevel($p.Id) | Where-Object { $_.Visible -and $_.Title -eq 'TrayClose' })
            if ($back.Count -gt 0) { break }
            Start-Sleep -Milliseconds 300
        }
        Check 'the running copy came back from the tray' ($null -ne $back -and $back.Count -gt 0)
    }
    Kill-App $p

    # ---------------------------------------------------------------- 13
    Scenario '13. Minimize goes to the tray when minimize-to-tray is on'
    $conf = "{ `"url`": `"$Fast`", `"window-type`": `"min+max+close+tray`", `"systray`": true, `"minimize-to-tray`": true, `"title`": `"TrayMin`", `"sleep-after`": `"off`" }"
    $dir = New-Case 'tray-min' $conf
    $p = Start-App $dir
    $w = (Wait-Window $p) | Where-Object { $_.Title -eq 'TrayMin' } | Select-Object -First 1
    Check 'the window opened' ($null -ne $w)
    if ($w) {
        [WwsWin]::SysCommand($w.Handle, 0xF020)   # SC_MINIMIZE
        Start-Sleep -Seconds 3
        $stillVisible = @([WwsWin]::TopLevel($p.Id) | Where-Object { $_.Visible -and $_.Title -eq 'TrayMin' })
        Check 'the window left the taskbar and the screen' ($stillVisible.Count -eq 0) "found $($stillVisible.Count)"
        if ($stillVisible.Count -ne 0) { Show-Windows $p }
        Check 'the process is still running' (-not $p.HasExited)
    }
    Kill-App $p

    # ---------------------------------------------------------------- 14
    if (-not $IncludeSlow) {
        Scenario '14. sleep-after frees the browser  (skipped, pass -IncludeSlow)'
    }
    else {
        Scenario '14. sleep-after frees the browser while the window is hidden'
        # "focus" so that starting a second copy wakes the sleeping one instead of
        # showing the "already running" error box.
        $conf = "{ `"url`": `"$Fast`", `"window-type`": `"min+max+close+tray`", `"systray`": true, `"title`": `"Sleeper`", `"single-instance-action`": `"focus`", `"sleep-after`": 1 }"
        $dir = New-Case 'sleep' $conf
        $p = Start-App $dir
        $w = (Wait-Window $p) | Where-Object { $_.Title -eq 'Sleeper' } | Select-Object -First 1
        Check 'the window opened' ($null -ne $w)
        Start-Sleep -Seconds 6

        function Get-Children($parentId) {
            return @(Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue |
                Where-Object { $_.ParentProcessId -eq $parentId })
        }

        $awake = @(Get-Children $p.Id)
        Check "the embedded browser is running (found $($awake.Count))" ($awake.Count -gt 0)
        $memAwake = (Get-Process -Id $p.Id).WorkingSet64

        if ($w) { [WwsWin]::SysCommand($w.Handle, 0xF060) }   # hide to the tray, the sleep clock starts
        Write-Host '  ... waiting up to 2.5 minutes for the sleep timer' -ForegroundColor DarkGray

        $asleep = @()
        $deadline = (Get-Date).AddSeconds(150)
        while ((Get-Date) -lt $deadline) {
            $asleep = @(Get-Children $p.Id)
            if ($asleep.Count -eq 0) { break }
            Start-Sleep -Seconds 5
        }
        Check 'the browser processes were shut down' ($asleep.Count -eq 0) "still $($asleep.Count)"
        Check 'the process itself is still alive' (-not $p.HasExited)

        if (-not $p.HasExited) {
            $memAsleep = (Get-Process -Id $p.Id).WorkingSet64
            Check "memory dropped ($([math]::Round($memAwake/1MB,1)) MB -> $([math]::Round($memAsleep/1MB,1)) MB)" ($memAsleep -lt $memAwake)

            # waking it up must reload the same page
            $second = Start-App $dir
            if (-not $second.WaitForExit(15000)) { Kill-App $second }
            Check 'the waking copy exited quietly' ($second.HasExited -and $second.ExitCode -eq 0) "got '$($second.ExitCode)'"
            $woken = @()
            $deadline = (Get-Date).AddSeconds(40)
            while ((Get-Date) -lt $deadline) {
                $woken = @(Get-Children $p.Id)
                if ($woken.Count -gt 0) { break }
                Start-Sleep -Seconds 2
            }
            Check "opening it again restarted the browser (found $($woken.Count))" ($woken.Count -gt 0)
        }
        Kill-App $p
    }
}
finally {
    # Anything this run started and left behind, plus its browser children.
    $leftovers = @(Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -eq 'app' -and $_.Path -and $_.Path.StartsWith($WorkRoot) })
    foreach ($leftover in $leftovers) { Kill-App $leftover }

    if (-not $KeepFiles) {
        Start-Sleep -Seconds 1
        Remove-Item $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host "passed: $script:Passed   failed: $script:Failed" -ForegroundColor $(if ($script:Failed -eq 0) { 'Green' } else { 'Red' })
if ($script:Failed -gt 0) {
    Write-Host ''
    Write-Host 'Failed checks:' -ForegroundColor Red
    $script:Failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}
exit 0
