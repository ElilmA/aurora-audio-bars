using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseAudioVisualizer.Audio;
using MediaBrush = System.Windows.Media.Brush;

namespace MouseAudioVisualizer.Visual;

/// <summary>
/// 透明、无边框、置顶、点击穿透的悬浮层，渲染环形频谱并跟随鼠标。
/// </summary>
public sealed class CursorOverlay : Window
{
    private const int Size = 256;
    private const int PhysicalSize = 256; // 逻辑像素，实际尺寸按 DPI 缩放

    private readonly AudioEngine _engine;
    private readonly RingRenderer _renderer;
    private readonly WriteableBitmap _bitmap;
    private readonly DispatcherTimer _renderTimer;
    private IntPtr _hwnd;
    private int _dpiScale = 96;
    private bool _followMouse = true;
    private int _lastCursorX = int.MinValue;
    private int _lastCursorY = int.MinValue;

    public float Intensity { get; set; } = 1.0f;
    public float Opacity2 { get; set; } = 0.9f;
    public VisualStyle Style { get; set; } = VisualStyle.Ring;
    public bool FollowMouse
    {
        get => _followMouse;
        set { _followMouse = value; if (value) FollowCursor(); }
    }

    public CursorOverlay(AudioEngine engine)
    {
        _engine = engine;
        _renderer = new RingRenderer(Size, SpectrumEngine.BandCount);
        _bitmap = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32, null);

        Width = Size;
        Height = Size;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Left = double.NaN; // 由 SetWindowPos 控制

        var img = new System.Windows.Controls.Image
        {
            Source = _bitmap,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        Content = img;

        SourceInitialized += OnSourceInitialized;

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _renderTimer.Tick += (_, _) => Render();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetClickThrough(_hwnd);
        _dpiScale = Native.GetDpiForWindow(_hwnd);
        if (_followMouse) FollowCursor();
    }

    public void Start()
    {
        Show();
        _renderTimer.Start();
        if (_followMouse) FollowCursor();
    }

    public new void Close()
    {
        _renderTimer.Stop();
        base.Close();
    }

    /// <summary>以当前光标位置为中心移动窗口（物理像素坐标），仅在光标移动时调用 SetWindowPos。</summary>
    private void FollowCursor()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!Native.GetCursorPos(out var pt)) return;
        if (pt.X == _lastCursorX && pt.Y == _lastCursorY) return;
        _lastCursorX = pt.X;
        _lastCursorY = pt.Y;
        int half = PhysicalSize * _dpiScale / 96 / 2;
        int x = pt.X - half;
        int y = pt.Y - half;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    private void Render()
    {
        var frame = _engine.Current;
        if (frame == null) return;
        if (_followMouse) FollowCursor();
        _renderer.Draw(_bitmap, frame, Intensity, Opacity2, Style);
    }
}
