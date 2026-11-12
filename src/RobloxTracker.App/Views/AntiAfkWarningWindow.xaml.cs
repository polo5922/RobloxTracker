using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RobloxTracker.App.Views;

/// <summary>Prévient que Roblox va passer au premier plan, sans prendre le focus de la fenêtre active.</summary>
public partial class AntiAfkWarningWindow : Window
{
    private const int ExStyleIndex = -20;
    private const long ToolWindow = 0x80;
    private const long NoActivate = 0x08000000;
    private const double ScreenMargin = 16;

    private readonly TaskCompletionSource _skipped = new();

    public AntiAfkWarningWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, ExStyleIndex, GetWindowLongPtr(handle, ExStyleIndex) | ToolWindow | NoActivate);
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - ScreenMargin;
            Top = area.Bottom - ActualHeight - ScreenMargin;
        };
    }

    /// <summary>Retourne <c>false</c> si l'utilisateur a choisi de passer ce cycle.</summary>
    public async Task<bool> CountdownAsync(int seconds, CancellationToken ct)
    {
        for (var left = seconds; left > 0; left--)
        {
            Message.Text = $"Roblox va passer au premier plan dans {left} s.";
            var tick = Task.Delay(TimeSpan.FromSeconds(1), ct);
            if (await Task.WhenAny(tick, _skipped.Task) == _skipped.Task) return false;
            await tick;
        }
        return true;
    }

    private void OnSkip(object sender, RoutedEventArgs e) => _skipped.TrySetResult();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern long SetWindowLongPtr(IntPtr window, int index, long value);
}
