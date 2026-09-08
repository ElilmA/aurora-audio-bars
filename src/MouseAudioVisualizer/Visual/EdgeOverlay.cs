using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseAudioVisualizer.Audio;

namespace MouseAudioVisualizer.Visual;

/// <summary>
/// 屏幕左右两侧的竖向频谱条：宽约 1mm、高 = 虚拟屏幕全高、透明置顶、点击穿透。
/// </summary>
public sealed class EdgeOverlay : IDisposable
{
    private const double BarWidthMm = 1.0; // 目标物理宽度（毫米）

    private readonly AudioEngine _engine;
    private readonly BarWindow _left;
    private readonly BarWindow _right;
    private readonly DispatcherTimer _renderTimer;

    public float Intensity { get; set; } = 1.0f;
    public float Opacity2 { get; set; } = 0.9f;

    public EdgeOverlay(AudioEngine engine)
    {
        _engine = engine;

        int vsLeft = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        int vsTop = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        int vsWidth = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
        int vsHeight = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);

        const int dpi = 96;
        int barW = Math.Max(2, (int)Math.Round(BarWidthMm * dpi / 25.4)); // @96dpi ≈ 4px
        double wDip = barW * 96.0 / dpi;
        double hDip = vsHeight * 96.0 / dpi;

        _left = new BarWindow(barW, vsHeight, wDip, hDip);
        _right = new BarWindow(barW, vsHeight, wDip, hDip);

        _left.SetBarPosition(vsLeft, vsTop);
        _right.SetBarPosition(vsLeft + vsWidth - barW, vsTop);

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _renderTimer.Tick += (_, _) => Render();
    }

    public void Start()
    {
        _left.Show();
        _right.Show();
        _renderTimer.Start();
    }

    private void Render()
    {
        var frame = _engine.Current;
        if (frame == null) return;
        _left.Draw(frame, Intensity, Opacity2, flip: false);
        _right.Draw(frame, Intensity, Opacity2, flip: true);
    }

    public void Show()
    {
        _left.Show();
        _right.Show();
    }

    public void Hide()
    {
        _left.Hide();
        _right.Hide();
    }

    public void Dispose()
    {
        _renderTimer.Stop();
        _left.Close();
        _right.Close();
    }

    /// <summary>单个竖向条窗口。</summary>
    private sealed class BarWindow : System.Windows.Window
    {
        private readonly EdgeRenderer _renderer;
        private readonly WriteableBitmap _bitmap;
        private readonly System.Windows.Controls.Image _image;
        private IntPtr _hwnd;

        public BarWindow(int widthPhys, int heightPhys, double widthDip, double heightDip)
        {
            Width = widthDip;
            Height = heightDip;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;

            _renderer = new EdgeRenderer(widthPhys, heightPhys, SpectrumEngine.BandCount);
            _bitmap = new WriteableBitmap(widthPhys, heightPhys, 96, 96, PixelFormats.Pbgra32, null);
            _image = new System.Windows.Controls.Image
            {
                Source = _bitmap,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
            };
            Content = _image;
            SourceInitialized += OnSourceInitialized;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetClickThrough(_hwnd);
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            ApplyPosition();
        }

        private int _posX;
        private int _posY;
        private bool _hasPos;

        public void SetBarPosition(int xPhys, int yPhys)
        {
            _posX = xPhys;
            _posY = yPhys;
            _hasPos = true;
            ApplyPosition();
        }

        private void ApplyPosition()
        {
            if (!_hasPos || _hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, _posX, _posY, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public void Draw(SpectrumFrame frame, float intensity, float alpha, bool flip)
        {
            _renderer.Draw(_bitmap, frame, intensity, alpha, flip);
        }
    }
}
