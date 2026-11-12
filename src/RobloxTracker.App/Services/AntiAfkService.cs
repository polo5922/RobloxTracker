using System.Diagnostics;
using System.Runtime.InteropServices;
using RobloxTracker.App.Views;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.Services;

/// <summary>
/// Pendant une partie, appuie sur Espace dans Roblox à intervalle aléatoire pour éviter la déconnexion,
/// puis rend le focus à la fenêtre qui l'avait. Un avertissement prévient avant tout changement de fenêtre.
/// </summary>
public sealed class AntiAfkService(SettingsService settings) : IDisposable
{
    private readonly Random _random = new();
    private CancellationTokenSource? _cycle;
    private bool _playing;

    /// <summary>Démarre ou arrête le cycle selon qu'un jeu est en cours.</summary>
    public void Update(SessionInfo? session)
    {
        _playing = session is not null;
        Sync();
    }

    /// <summary>À appeler après un changement de réglage : le cycle repart avec un nouveau délai.</summary>
    public void Reset()
    {
        Stop();
        Sync();
    }

    private void Sync()
    {
        if (!_playing || !settings.Current.AntiAfkEnabled) Stop();
        else if (_cycle is null)
        {
            _cycle = new CancellationTokenSource();
            _ = RunAsync(_cycle.Token);
        }
    }

    private void Stop()
    {
        _cycle?.Cancel();
        _cycle?.Dispose();
        _cycle = null;
    }

    // Reste sur le thread UI : les await reprennent sur le Dispatcher.
    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var delay = AntiAfkSchedule.NextDelay(settings.Current.AntiAfkMinMinutes, settings.Current.AntiAfkMaxMinutes, _random);
                await Task.Delay(delay - TimeSpan.FromSeconds(AntiAfkSchedule.WarningSeconds), ct);

                try
                {
                    await RunOnceAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un cycle raté ne doit pas arrêter les suivants.
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task RunOnceAsync(CancellationToken ct)
    {
        var roblox = FindRobloxWindow();
        if (roblox == IntPtr.Zero) return;

        var action = NextAction(roblox);
        if (action == AntiAfkAction.SwitchWindow)
        {
            var warning = new AntiAfkWarningWindow();
            warning.Show();
            try
            {
                if (!await warning.CountdownAsync(AntiAfkSchedule.WarningSeconds, ct)) return;
            }
            finally
            {
                warning.Close();
            }

            // L'utilisateur a pu revenir sur Roblox, ou le fermer, pendant le compte à rebours.
            if (!IsWindow(roblox)) return;
            action = NextAction(roblox);
        }

        if (action == AntiAfkAction.PressInPlace) await PressSpaceAsync();
        else if (action == AntiAfkAction.SwitchWindow) await PressInBackgroundAsync(roblox);
    }

    private static AntiAfkAction NextAction(IntPtr roblox) =>
        AntiAfkSchedule.Decide(GetForegroundWindow() == roblox, IdleTime());

    /// <summary>Passe sur Roblox, appuie, puis revient. Non annulable : la fenêtre d'origine doit toujours être rendue.</summary>
    private static async Task PressInBackgroundAsync(IntPtr roblox)
    {
        var previous = GetForegroundWindow();
        var wasMinimized = IsIconic(roblox);
        try
        {
            if (wasMinimized) ShowWindow(roblox, SwRestore);
            if (!await FocusAsync(roblox)) return;

            // Roblox lit le clavier à chaque image : il lui faut un instant une fois le focus reçu.
            await Task.Delay(wasMinimized ? 400 : 150);
            await PressSpaceAsync();
            await Task.Delay(50);
        }
        finally
        {
            if (previous != IntPtr.Zero && IsWindow(previous)) await FocusAsync(previous);
            if (wasMinimized) ShowWindow(roblox, SwShowMinNoActive);
        }
    }

    private static async Task<bool> FocusAsync(IntPtr window)
    {
        // Windows refuse le premier plan à une app en arrière-plan, sauf si elle partage la file d'entrée de la fenêtre active.
        var current = GetCurrentThreadId();
        var foreground = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        var attached = foreground != 0 && foreground != current && AttachThreadInput(current, foreground, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached) AttachThreadInput(current, foreground, false);
        }

        for (var i = 0; i < 10 && GetForegroundWindow() != window; i++) await Task.Delay(20);
        return GetForegroundWindow() == window;
    }

    private static async Task PressSpaceAsync()
    {
        SendKey(KeyEventScanCode);
        try
        {
            await Task.Delay(60);
        }
        finally
        {
            SendKey(KeyEventScanCode | KeyEventKeyUp);
        }
    }

    // Roblox lit le clavier en entrée brute : il faut un vrai scancode, pas un message WM_KEYDOWN.
    private static void SendKey(uint flags)
    {
        var input = new Input { Type = InputKeyboard, Data = new InputData { Keyboard = new KeyboardInput { ScanCode = SpaceScanCode, Flags = flags } } };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    private static IntPtr FindRobloxWindow()
    {
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        try
        {
            return processes.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    private static TimeSpan IdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info)
            ? TimeSpan.FromMilliseconds((uint)Environment.TickCount - info.Time)
            : TimeSpan.Zero;
    }

    public void Dispose() => Stop();

    private const int SwRestore = 9;
    private const int SwShowMinNoActive = 7;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x2;
    private const uint KeyEventScanCode = 0x8;
    private const ushort SpaceScanCode = 0x39;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // MOUSEINPUT est le plus grand membre de l'union : il fixe la taille attendue par SendInput.
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
