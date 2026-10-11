using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WukongBenchmarkRunner;

/// Эмуляция ввода в окне игры: клавиши (скан-коды) и клики мышью по относительным координатам окна.
/// Окно игры выводится на передний план перед каждым действием.
public static class Keyboard
{
    // --- WinAPI ---
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    // MOUSEINPUT в объединении нужен, чтобы размер INPUT был правильным (на x64 = 40 байт)
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint KeyEventExtended = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;

    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseAbsolute = 0x8000;

    private const int HoldMs = 250;

    static Keyboard()
    {
        // Без этого на экранах с масштабом 125%/150% координаты мыши и окна расходятся
        SetProcessDPIAware();
    }

    // Скан-коды (набор 1). Для стрелок нужен флаг "extended".
    private static readonly Dictionary<string, (ushort Scan, bool Extended)> Keys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Escape"] = (0x01, false),
            ["Enter"] = (0x1C, false),
            ["Space"] = (0x39, false),
            ["Tab"] = (0x0F, false),
            ["Up"] = (0x48, true),
            ["Down"] = (0x50, true),
            ["Left"] = (0x4B, true),
            ["Right"] = (0x4D, true),
        };

    /// Выполняет сценарий. Для каждого шага: пауза, окно игры вперёд, затем нажатие клавиши
    /// или (если Key = "Click") клик мышью в точке (X, Y) - доли от размера окна, 0..1.
    public static async Task RunStepsAsync(Process proc, List<KeyStep> steps)
    {
        await WaitForWindowAsync(proc, 120);
        bool warned = false;

        foreach (var step in steps)
        {
            await Task.Delay(step.DelayBeforeMs);

            for (int n = 0; n < Math.Max(1, step.Repeat); n++)
            {
                if (proc.HasExited) return;

                bool foreground = FocusWindow(proc);
                await Task.Delay(400); // дать окну время получить фокус
                if (!foreground && !warned)
                {
                    Console.WriteLine("! Окно игры не стало активным, ввод может не дойти до игры.");
                    warned = true;
                }

                if (step.Key.Equals("Click", StringComparison.OrdinalIgnoreCase))
                {
                    var (px, py) = ToScreen(proc, step.X, step.Y);
                    Click(px, py);
                }
                else
                {
                    Press(step.Key);
                }

                if (n < step.Repeat - 1) await Task.Delay(step.RepeatDelayMs);
            }
        }
    }

    /// Окно игры создаётся не сразу после запуска процесса, ждём его появления.
    private static async Task WaitForWindowAsync(Process proc, int timeoutSec)
    {
        for (int i = 0; i < timeoutSec && !proc.HasExited; i++)
        {
            proc.Refresh();
            if (proc.MainWindowHandle != IntPtr.Zero) return;
            await Task.Delay(1000);
        }
        Console.WriteLine("  ! окно игры не найдено, ввод может уйти в другое окно");
    }

    /// Выводит окно игры на передний план. Возвращает true, если оно действительно стало активным.
    public static bool FocusWindow(Process proc)
    {
        proc.Refresh();
        var hWnd = proc.MainWindowHandle;
        if (hWnd == IntPtr.Zero) return false;

        // Windows запрещает фоновым процессам красть фокус. Обходим: лёгкое нажатие Alt
        // и временное подключение к потоку текущего активного окна.
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, KeyEventKeyUp, UIntPtr.Zero);

        var foreground = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
        uint thisThread = GetCurrentThreadId();

        bool attached = foregroundThread != thisThread && AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            ShowWindow(hWnd, 9); // SW_RESTORE
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached) AttachThreadInput(thisThread, foregroundThread, false);
        }

        return GetForegroundWindow() == hWnd;
    }

    // ---------------- Клавиатура ----------------

    public static void Press(string keyName)
    {
        if (!Keys.TryGetValue(keyName, out var key))
            throw new ArgumentException($"Неизвестная клавиша \"{keyName}\". Доступны: {string.Join(", ", Keys.Keys)}, Click.");

        SendKey(key.Scan, key.Extended, keyUp: false);
        Thread.Sleep(HoldMs);
        SendKey(key.Scan, key.Extended, keyUp: true);
    }

    private static void SendKey(ushort scan, bool extended, bool keyUp)
    {
        uint flags = KeyEventScanCode;
        if (extended) flags |= KeyEventExtended;
        if (keyUp) flags |= KeyEventKeyUp;

        var input = new INPUT
        {
            type = InputKeyboard,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags } }
        };
        Send(input);
    }

    // ---------------- Мышь ----------------

    /// Переводит доли окна (0..1) в пиксели экрана. Если окна нет, считаем от основного монитора.
    public static (int X, int Y) ToScreen(Process proc, double fx, double fy)
    {
        proc.Refresh();
        var hWnd = proc.MainWindowHandle;
        if (hWnd != IntPtr.Zero && GetClientRect(hWnd, out var rect) && rect.Right > 0 && rect.Bottom > 0)
        {
            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hWnd, ref origin);
            return (origin.X + (int)(fx * rect.Right), origin.Y + (int)(fy * rect.Bottom));
        }
        return ((int)(fx * GetSystemMetrics(0)), (int)(fy * GetSystemMetrics(1)));
    }

    /// Клик с "наведением": интерфейс игры подсвечивает кнопку и принимает клик, только если до этого
    /// пришло движение мыши, поэтому сначала подводим курсор в два приёма, потом нажимаем.
    public static void Click(int x, int y)
    {
        MoveTo(x - 4, y - 4);
        Thread.Sleep(150);
        MoveTo(x, y);
        Thread.Sleep(400);
        SendMouse(MouseLeftDown);
        Thread.Sleep(150);
        SendMouse(MouseLeftUp);
    }

    private static void MoveTo(int x, int y)
    {
        int sw = Math.Max(2, GetSystemMetrics(0));
        int sh = Math.Max(2, GetSystemMetrics(1));
        int nx = (int)Math.Clamp(Math.Round(x * 65535.0 / (sw - 1)), 0, 65535);
        int ny = (int)Math.Clamp(Math.Round(y * 65535.0 / (sh - 1)), 0, 65535);
        SendMouse(MouseMove | MouseAbsolute, nx, ny);
    }

    private static void SendMouse(uint flags, int dx = 0, int dy = 0)
    {
        var input = new INPUT
        {
            type = InputMouse,
            u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } }
        };
        Send(input);
    }

    private static void Send(INPUT input)
    {
        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "SendInput не сработал (если бенчмарк запущен от администратора, запустить инструмент необходимо от администратора).");
    }
}
