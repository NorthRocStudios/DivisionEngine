#
# Small Windows UI driver used to smoke-test the Division Engine node editor.
# Each invocation runs in a fresh pwsh process so the P/Invoke helpers never collide.
#
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('screenshot', 'click', 'doubleclick', 'rightclick', 'wheel', 'drag', 'middledrag', 'down', 'move', 'up', 'key', 'focus', 'rect')]
    [string]$Action,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$X2 = 0,
    [int]$Y2 = 0,
    [int]$Width = 1600,
    [int]$Height = 1000,
    [int]$Delta = 120,
    [string]$Out = 'artifacts/node-editor/capture.png',
    [string]$ProcessName = 'DivisionEngine.Editor',
    [string]$Keys = '',
    [string]$WindowTitle = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DivisionUiNative
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static IntPtr FindWindowByTitle(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            var text = new System.Text.StringBuilder(512);
            GetWindowText(h, text, 512);
            if (string.Equals(text.ToString(), title, StringComparison.Ordinal))
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public const uint LeftDown = 0x0002;
    public const uint LeftUp = 0x0004;
    public const uint MiddleDown = 0x0020;
    public const uint MiddleUp = 0x0040;
    public const uint Wheel = 0x0800;

    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(90);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(70);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
    }

    public static void RightClick(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(90);
        mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(70);
        mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero);
    }

    public static void Down(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(90);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
    }

    public static void Move(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
    }

    public static void Up()
    {
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
    }

    public static void MouseWheel(int x, int y, int delta)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(90);
        mouse_event(Wheel, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
    }

    public static void Drag(int x1, int y1, int x2, int y2, int steps)
    {
        SetCursorPos(x1, y1);
        System.Threading.Thread.Sleep(120);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(120);
        for (int i = 1; i <= steps; i++)
        {
            int x = x1 + (x2 - x1) * i / steps;
            int y = y1 + (y2 - y1) * i / steps;
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(30);
        }
        System.Threading.Thread.Sleep(100);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
    }

    public static void MiddleDrag(int x1, int y1, int x2, int y2, int steps)
    {
        SetCursorPos(x1, y1);
        System.Threading.Thread.Sleep(120);
        mouse_event(MiddleDown, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(120);
        for (int i = 1; i <= steps; i++)
        {
            int x = x1 + (x2 - x1) * i / steps;
            int y = y1 + (y2 - y1) * i / steps;
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(30);
        }
        System.Threading.Thread.Sleep(100);
        mouse_event(MiddleUp, 0, 0, 0, UIntPtr.Zero);
    }
}
'@

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($null -eq $process) { throw "No window found for process '$ProcessName'." }
$handle = $process.MainWindowHandle
[DivisionUiNative]::SetForegroundWindow($handle) | Out-Null

function Save-Screenshot([string]$path, [int]$width, [int]$height)
{
    $dir = Split-Path -Parent (Join-Path (Get-Location) $path)
    if (-not [string]::IsNullOrWhiteSpace($dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen(0, 0, 0, 0, $bitmap.Size)
    $bitmap.Save((Join-Path (Get-Location) $path), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Host "saved $path"
}

switch ($Action)
{
    'focus' {
        [DivisionUiNative]::ShowWindow($handle, 9) | Out-Null
        [DivisionUiNative]::SetForegroundWindow($handle) | Out-Null
        Write-Host 'focused'
    }
    'rect' {
        [DivisionUiNative]::ShowWindow($handle, 9) | Out-Null
        [DivisionUiNative]::MoveWindow($handle, $X, $Y, $Width, $Height, $true) | Out-Null
        Write-Host "moved to $X,$Y ${Width}x${Height}"
        Start-Sleep -Milliseconds 500
    }
    'click' {
        [DivisionUiNative]::Click($X, $Y)
        Write-Host "clicked $X,$Y"
    }
    'doubleclick' {
        [DivisionUiNative]::Click($X, $Y)
        Start-Sleep -Milliseconds 90
        [DivisionUiNative]::Click($X, $Y)
        Write-Host "double-clicked $X,$Y"
    }
    'rightclick' {
        [DivisionUiNative]::RightClick($X, $Y)
        Write-Host "right-clicked $X,$Y"
    }
    'down' {
        [DivisionUiNative]::Down($X, $Y)
        Write-Host "left down at $X,$Y"
    }
    'move' {
        [DivisionUiNative]::Move($X, $Y)
        Write-Host "moved to $X,$Y"
    }
    'up' {
        [DivisionUiNative]::Up()
        Write-Host 'left up'
    }
    'wheel' {
        [DivisionUiNative]::MouseWheel($X, $Y, $Delta)
        Write-Host "wheel $Delta at $X,$Y"
    }
    'drag' {
        [DivisionUiNative]::Drag($X, $Y, $X2, $Y2, 20)
        Write-Host "dragged $X,$Y -> $X2,$Y2"
    }
    'middledrag' {
        [DivisionUiNative]::MiddleDrag($X, $Y, $X2, $Y2, 20)
        Write-Host "middle-dragged $X,$Y -> $X2,$Y2"
    }
    'key' {
        if (-not [string]::IsNullOrWhiteSpace($WindowTitle)) {
            $target = [DivisionUiNative]::FindWindowByTitle($WindowTitle)
            if ($target -eq [IntPtr]::Zero) { throw "Window '$WindowTitle' not found." }
            [DivisionUiNative]::SetForegroundWindow($target) | Out-Null
            Start-Sleep -Milliseconds 250
        }
        [System.Windows.Forms.SendKeys]::SendWait($Keys)
        Write-Host "sent keys $Keys"
    }
    'screenshot' {
        Save-Screenshot $Out $Width $Height
    }
}

Start-Sleep -Milliseconds 250
