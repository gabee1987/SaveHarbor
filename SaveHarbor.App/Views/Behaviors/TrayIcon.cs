using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SaveHarbor.App.Localization;

namespace SaveHarbor.App.Views.Behaviors;

// The notification-area icon while the window is hidden during play: a tooltip, a balloon for shared progress and
// failures, double-click to open, and a menu with Open and Exit. A small Shell_NotifyIcon wrapper, so no Windows Forms.
public sealed class TrayIcon : IDisposable
{
    private const int NimAdd = 0x0;
    private const int NimModify = 0x1;
    private const int NimDelete = 0x2;
    private const int NifMessage = 0x1;
    private const int NifIcon = 0x2;
    private const int NifTip = 0x4;
    private const int NifInfo = 0x10;
    private const int NiifInfo = 0x1;
    private const int WmApp = 0x8000;
    private const int CallbackMessage = WmApp + 1;
    private const int WmLeftButtonDoubleClick = 0x203;
    private const int WmRightButtonUp = 0x205;
    private const int BalloonUserClick = 0x405;
    private const int IconId = 1;
    private const int ImageIcon = 1;
    private const int SmallIconWidth = 49;
    private const int SmallIconHeight = 50;

    // The icon the compiler embeds from <ApplicationIcon> has this resource id; it is also the id of Windows' generic
    // application icon, the fallback.
    private static readonly IntPtr ApplicationIcon = new(32512);

    private readonly HwndSource source;
    private readonly int taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
    private readonly IntPtr ownIcon = LoadOwnIcon();
    private string tooltip = string.Empty;
    private bool isShown;

    public TrayIcon(Window window)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle())
            ?? throw new InvalidOperationException("The window has no handle.");
        source.AddHook(OnMessage);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public void Show(string text)
    {
        tooltip = text;
        var data = Data(NifMessage | NifIcon | NifTip);
        isShown = Shell_NotifyIconW(isShown ? NimModify : NimAdd, ref data) || isShown;
    }

    public void Hide()
    {
        if (!isShown)
        {
            return;
        }

        var data = Data(0);
        Shell_NotifyIconW(NimDelete, ref data);
        isShown = false;
    }

    public void Balloon(string title, string text)
    {
        if (!isShown)
        {
            return;
        }

        var data = Data(NifInfo);
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = NiifInfo;
        Shell_NotifyIconW(NimModify, ref data);
    }

    public void Dispose()
    {
        Hide();
        source.RemoveHook(OnMessage);
        if (ownIcon != IntPtr.Zero)
        {
            DestroyIcon(ownIcon);
        }
    }

    // SaveHarbor's icon at the notification area's size, so Windows does not scale a larger image down.
    private static IntPtr LoadOwnIcon() =>
        LoadImageW(GetModuleHandleW(null), ApplicationIcon, ImageIcon, GetSystemMetrics(SmallIconWidth), GetSystemMetrics(SmallIconHeight), 0);

    private NotifyIconData Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = source.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = ownIcon != IntPtr.Zero ? ownIcon : LoadIconW(IntPtr.Zero, ApplicationIcon),
        szTip = Truncate(tooltip, 127),
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Explorer restarted: the icon is gone and has to be added again.
        if (message == taskbarCreatedMessage && isShown)
        {
            isShown = false;
            Show(tooltip);
            return IntPtr.Zero;
        }

        if (message != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        switch ((int)lParam & 0xFFFF)
        {
            case WmLeftButtonDoubleClick or BalloonUserClick:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
            case WmRightButtonUp:
                ShowMenu();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var open = new MenuItem { Header = UiTextCatalog.Get("Tray.Open"), FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        var exit = new MenuItem { Header = UiTextCatalog.Get("Tray.Exit") };
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(open);
        menu.Items.Add(exit);

        // Without the foreground, a click elsewhere would not close the menu.
        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource popup)
            {
                SetForegroundWindow(popup.Handle);
            }
        };
        menu.IsOpen = true;
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr instance, IntPtr name, int type, int width, int height, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
