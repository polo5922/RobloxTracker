using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RobloxTracker.App;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private const int WmSysCommand = 0x0112;
    private const int ScClose = 0xF060;

    private bool _userClosing;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Si renseigné et qu'il retourne true, la fermeture est annulée (réduction dans la zone de notification).</summary>
    public Func<bool>? InterceptClose { get; set; }

    /// <summary>Barre de titre sombre, assortie au menu latéral (Windows 10 20H1+ / Windows 11).</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        var caption = 0x001A1411; // COLORREF (0x00BBGGRR) de SidebarColor #11141A
        DwmSetWindowAttribute(hwnd, DwmCaptionColor, ref caption, sizeof(int)); // ignoré avant Windows 11
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    /// <summary>
    /// Croix, Alt+F4 et barre des tâches passent par SC_CLOSE ; un WM_CLOSE direct vient d'un autre programme
    /// (l'installateur lors d'une mise à jour) et doit vraiment fermer l'app.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScClose) _userClosing = true;
        return IntPtr.Zero;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var userClosing = _userClosing;
        _userClosing = false;
        if (userClosing && InterceptClose?.Invoke() == true)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
