using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MouseAudioVisualizer.Audio;

namespace MouseAudioVisualizer.Visual;

/// <summary>
/// 屏幕左右两侧的竖向频谱条：宽可调（默认 1mm）、高 = 虚拟屏幕全高、透明置顶、点击穿透。
/// 分辨率/DPI 适配：每个条窗口都基于自身真实 DPI（GetDpiForWindow）换算物理尺寸，
/// 使能量条始终从「虚拟屏幕实际底部」贴边、全高；显示器变化或 DPI 变化时自动重算。
/// </summary>
public sealed class EdgeOverlay : IDisposable
{
    private const double DefaultBarWidthMm = 1.0;

    private readonly AudioEngine _engine;
    private BarWindow? _left;
    private BarWindow? _right;
    private readonly DispatcherTimer _renderTimer;

    public float Intensity { get; set; } = 1.0f;
    public float Opacity2 { get; set; } = 0.9f;

    /// <summary>当前条宽（毫米档位）。</summary>
    public double BarWidthMm => _barWidthMm;

    private double _barWidthMm = DefaultBarWidthMm;

    public EdgeOverlay(AudioEngine engine)
    {
        _engine = engine;
        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16), // 60 FPS：快速响应音频
        };
        _renderTimer.Tick += (_, _) => Render();

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.UserPreferenceChanged += OnDisplayChanged;
        CreateWindows();
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Relayout();

    private static (int left, int top, int width, int height) VirtualScreen()
    {
        return (
            Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN),
            Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN),
            Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN),
            Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));
    }

    private void CreateWindows()
    {
        var (vsL, vsT, vsW, vsH) = VirtualScreen();

        _left?.Close();
        _right?.Close();

        // 左右同色谱、不同相位：左以紫(270°)起底，右以青(180°)起底（视觉平衡）
        _left = new BarWindow(_engine, isRight: false, hueStart: 270f);
        _right = new BarWindow(_engine, isRight: true, hueStart: 180f);

        _left.Configure(_barWidthMm, vsL, vsT, vsH);
        _right.Configure(_barWidthMm, vsL + vsW, vsT, vsH); // 宽由窗口自身 DPI 决定，这里只给右边缘锚点
    }

    private void Relayout()
    {
        var (vsL, vsT, vsW, vsH) = VirtualScreen();
        _left?.Configure(_barWidthMm, vsL, vsT, vsH);
        _right?.Configure(_barWidthMm, vsL + vsW, vsT, vsH);
        _left?.Reposition();
        _right?.Reposition();
    }

    /// <summary>设置条宽（毫米），即时重建并定位窗口。</summary>
    public void SetBarWidthMm(double mm)
    {
        if (Math.Abs(mm - _barWidthMm) < 0.001) return;
        _barWidthMm = mm;
        var (vsL, vsT, vsW, vsH) = VirtualScreen();
        _left?.Configure(mm, vsL, vsT, vsH);
        _right?.Configure(mm, vsL + vsW, vsT, vsH);
    }

    public void Start()
    {
        _left?.Show();
        _right?.Show();
        _renderTimer.Start();
    }

    private void Render()
    {
        var frame = _engine.Current;
        if (frame == null) return;
        _left?.Draw(frame, Intensity, Opacity2);
        _right?.Draw(frame, Intensity, Opacity2);
    }

    public void Show()
    {
        _left?.Show();
        _right?.Show();
    }

    public void Hide()
    {
        _left?.Hide();
        _right?.Hide();
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.UserPreferenceChanged -= OnDisplayChanged;
        _renderTimer.Stop();
        _left?.Close();
        _right?.Close();
    }

    /// <summary>单个竖向条窗口：负责用真实 DPI 换算物理尺寸并对齐虚拟屏幕底部。</summary>
    private sealed class BarWindow : System.Windows.Window
    {
        private EdgeRenderer? _renderer;
        private WriteableBitmap? _bitmap;
        private readonly System.Windows.Controls.Image _image;
        private IntPtr _hwnd;
        private readonly float _hueStart;
        private readonly AudioEngine _engine;
        private readonly bool _isRight;

        private double _mm = DefaultBarWidthMm;
        private int _anchorLeft;    // 左缘锚点（物理）：左条=虚拟屏左缘，右条=虚拟屏右缘
        private int _anchorTop;     // 顶 y（物理）
        private int _vsHeightPx;    // 虚拟屏幕物理高
        private bool _configured;

        public BarWindow(AudioEngine engine, bool isRight, float hueStart)
        {
            _engine = engine;
            _isRight = isRight;
            _hueStart = hueStart;

            Width = 10;
            Height = 100;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;

            _image = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
            };
            Content = _image;
            SourceInitialized += OnSourceInitialized;
            DpiChanged += OnDpiChanged;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetClickThrough(_hwnd);
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Apply();
        }

        private void OnDpiChanged(object? sender, System.Windows.DpiChangedEventArgs e)
        {
            // 屏幕缩放变化 → 物理尺寸按新 DPI 重建
            Apply();
        }

        /// <summary>
        /// 设定目标：锚点 + 虚拟屏幕物理高。宽（mm）与 DPI 由窗口自身决定。
        /// 左条 anchorLeft=虚拟屏左缘；右条 anchorLeft=虚拟屏右缘（宽取负方向）。
        /// </summary>
        public void Configure(double mm, int anchorLeft, int anchorTop, int vsHeightPx)
        {
            _mm = mm;
            _anchorLeft = anchorLeft;
            _anchorTop = anchorTop;
            _vsHeightPx = vsHeightPx;
            _configured = true;
            Apply();
        }

        /// <summary>仅重定位（物理坐标不变时窗口被系统挪动后恢复）。</summary>
        public void Reposition()
        {
            if (!_configured || _hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, XPhys, YPhys, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        private int Dpi => _hwnd == IntPtr.Zero ? 96 : Native.GetDpiForWindow(_hwnd);

        private int WidthPhys => Math.Max(2, (int)Math.Round(_mm * Dpi / 25.4));

        // 左条贴虚拟屏左缘；右条 anchorLeft=虚拟屏右缘坐标 → 向左取宽
        private int XPhys => _isRight ? _anchorLeft - WidthPhys : _anchorLeft;
        private int YPhys => _anchorTop;
        private int HeightPhys => Math.Max(2, _vsHeightPx);

        private void Apply()
        {
            if (!_configured) return;
            int w = WidthPhys;
            int h = HeightPhys;
            int dpi = Dpi;

            // WPF 窗口用 DIP；转成与真实 DPI 匹配的逻辑尺寸 → 物理尺寸精确
            Width = w * 96.0 / dpi;
            Height = h * 96.0 / dpi;

            _renderer = new EdgeRenderer(w, h, SpectrumEngine.BandCount, _hueStart);
            _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            _image.Source = _bitmap;

            if (_hwnd != IntPtr.Zero)
            {
                // 以物理像素精确定位/定尺寸，确保贴边且不依赖 WPF 布局时机
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, XPhys, YPhys, w, h,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
        }

        public void Draw(SpectrumFrame frame, float intensity, float alpha)
        {
            if (_renderer != null && _bitmap != null)
            {
                _renderer.Draw(_bitmap, frame, intensity, alpha);
            }
        }
    }
}
