using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseAudioVisualizer.Audio;

namespace MouseAudioVisualizer.Visual;

/// <summary>
/// 屏幕左右两侧的竖向频谱条：宽度可调（默认 1mm）、高 = 虚拟屏幕全高、透明置顶、点击穿透。
/// 左右两侧渲染一致（彩色连续渐变）。
/// </summary>
public sealed class EdgeOverlay : IDisposable
{
    private const double DefaultBarWidthMm = 1.0;

    private readonly AudioEngine _engine;
    private BarWindow _left = null!;
    private BarWindow _right = null!;
    private readonly DispatcherTimer _renderTimer;

    private readonly int _vsLeft;
    private readonly int _vsTop;
    private readonly int _vsWidth;
    private readonly int _vsHeight;
    private int _dpi = 96;
    private double _barWidthMmApprox = DefaultBarWidthMm;

    public float Intensity { get; set; } = 1.0f;
    public float Opacity2 { get; set; } = 0.9f;

    /// <summary>当前条宽（毫米档位）。</summary>
    public double BarWidthMm => _barWidthMmApprox;

    public EdgeOverlay(AudioEngine engine)
    {
        _engine = engine;

        _vsLeft = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        _vsTop = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        _vsWidth = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
        _vsHeight = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);

        CreateWindows();
        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16), // 60 FPS：快速响应音频
        };
        _renderTimer.Tick += (_, _) => Render();
    }

    private void CreateWindows()
    {
        int barW = MmToPx(DefaultBarWidthMm);
        double wDip = barW * 96.0 / _dpi;
        double hDip = _vsHeight * 96.0 / _dpi;

        _left?.Close();
        _right?.Close();
        // 左右同色谱、不同相位：左以紫(270°)起底，右以青(180°)起底（视觉平衡）
        _left = new BarWindow(barW, _vsHeight, wDip, hDip, 270f);
        _right = new BarWindow(barW, _vsHeight, wDip, hDip, 180f);
        _left.SetBarPosition(_vsLeft, _vsTop);
        _right.SetBarPosition(_vsLeft + _vsWidth - barW, _vsTop);
    }

    private int MmToPx(double mm) => Math.Max(2, (int)Math.Round(mm * _dpi / 25.4));

    /// <summary>设置条宽（毫米），即时重建并定位窗口。</summary>
    public void SetBarWidthMm(double mm)
    {
        if (Math.Abs(mm - _barWidthMmApprox) < 0.001) return;
        _barWidthMmApprox = mm;
        int barW = MmToPx(mm);
        double wDip = barW * 96.0 / _dpi;

        _left.ResizeBar(barW, wDip, _vsLeft, _vsTop);
        _right.ResizeBar(barW, wDip, _vsLeft + _vsWidth - barW, _vsTop);
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
        _left.Draw(frame, Intensity, Opacity2);
        _right.Draw(frame, Intensity, Opacity2);
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
        private EdgeRenderer _renderer;
        private WriteableBitmap _bitmap;
        private readonly System.Windows.Controls.Image _image;
        private IntPtr _hwnd;
        private int _physWidth;
        private readonly int _physHeight;

        public BarWindow(int widthPhys, int heightPhys, double widthDip, double heightDip, float hueStart)
        {
            _physWidth = widthPhys;
            _physHeight = heightPhys;
            _hueStart = hueStart;
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

            _renderer = new EdgeRenderer(widthPhys, heightPhys, SpectrumEngine.BandCount, _hueStart);
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
        private readonly float _hueStart;

        public void SetBarPosition(int xPhys, int yPhys)
        {
            _posX = xPhys;
            _posY = yPhys;
            _hasPos = true;
            ApplyPosition();
        }

        public void ResizeBar(int widthPhys, double widthDip, int xPhys, int yPhys)
        {
            _physWidth = widthPhys;
            Width = widthDip;
            _renderer = new EdgeRenderer(widthPhys, _physHeight, SpectrumEngine.BandCount, _hueStart);
            _bitmap = new WriteableBitmap(widthPhys, _physHeight, 96, 96, PixelFormats.Pbgra32, null);
            _image.Source = _bitmap;
            SetBarPosition(xPhys, yPhys);
        }

        private void ApplyPosition()
        {
            if (!_hasPos || _hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, _posX, _posY, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public void Draw(SpectrumFrame frame, float intensity, float alpha)
        {
            _renderer.Draw(_bitmap, frame, intensity, alpha);
        }
    }
}