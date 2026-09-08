using System.Windows;
using MouseAudioVisualizer.Audio;
using MouseAudioVisualizer.Visual;

namespace MouseAudioVisualizer;

/// <summary>应用宿主：组装音频引擎、悬浮层与托盘控制。</summary>
public sealed class AppHost : IDisposable
{
    private readonly AudioEngine _engine;
    private CursorOverlay? _overlay;
    private Shell.TrayIcon? _tray;
    private bool _enabled = true;

    public bool IsEnabled => _enabled;

    public AppHost()
    {
        Native.TrySetDpiAwareness();
        _engine = new AudioEngine();
        _engine.Error += ex => System.Diagnostics.Debug.WriteLine($"[AudioEngine] {ex}");
    }

    public void Start()
    {
        _engine.Start();
        _overlay = new CursorOverlay(_engine) { Intensity = 1.0f, Opacity2 = 0.9f };
        _overlay.Start();
        _tray = new Shell.TrayIcon(this);
    }

    public void Toggle()
    {
        _enabled = !_enabled;
        if (_overlay != null)
        {
            if (_enabled) _overlay.Show();
            else _overlay.Hide();
        }
    }

    public void SetIntensity(float value)
    {
        if (_overlay != null) _overlay.Intensity = value;
    }

    public void SetOpacity(float value)
    {
        if (_overlay != null) _overlay.Opacity2 = value;
    }

    public void SetStyle(VisualStyle style)
    {
        if (_overlay != null) _overlay.Style = style;
    }

    public void SetAutoStart(bool enabled)
    {
        Shell.TrayIcon.SetAutoStart(enabled);
    }

    public bool IsAutoStartEnabled => Shell.TrayIcon.IsAutoStartEnabled;

    public void Dispose()
    {
        _tray?.Dispose();
        _overlay?.Close();
        _engine.Dispose();
    }
}

/// <summary>入口：不显示主窗口，仅悬浮层 + 托盘。</summary>
public static class Program
{
    private static AppHost? _host;

    [STAThread]
    public static void Main()
    {
        var app = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        app.Startup += (_, _) =>
        {
            _host = new AppHost();
            _host.Start();
        };
        app.Exit += (_, _) => _host?.Dispose();
        app.Run();
    }
}
