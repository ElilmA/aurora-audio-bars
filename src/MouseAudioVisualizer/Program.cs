using System.Windows;
using MouseAudioVisualizer.Audio;
using MouseAudioVisualizer.Visual;

namespace MouseAudioVisualizer;

public sealed class AppHost : IDisposable
{
    private readonly AudioEngine _engine;
    private EdgeOverlay? _overlay;
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
        _overlay = new EdgeOverlay(_engine) { Intensity = 1.0f, Opacity2 = 0.9f };
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

    public void SetBarWidthMm(double mm)
    {
        if (_overlay != null) _overlay.SetBarWidthMm(mm);
    }

    public double BarWidthMm => _overlay?.BarWidthMm ?? 1.0;

    public void SetAutoStart(bool enabled)
    {
        Shell.TrayIcon.SetAutoStart(enabled);
    }

    public bool IsAutoStartEnabled => Shell.TrayIcon.IsAutoStartEnabled;

    public void Dispose()
    {
        _tray?.Dispose();
        _overlay?.Dispose();
        _engine.Dispose();
    }
}

/// <summary>入口：不显示主窗口，仅悬浮层 + 托盘。</summary>
public static class Program
{
    private static AppHost? _host;

    [STAThread]
    public static void Main(string[] args)
    {
        double? barWidthMm = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--bar-width-mm" && double.TryParse(args[i + 1], out double mm))
            {
                barWidthMm = mm;
            }
        }

        var app = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        app.Startup += (_, _) =>
        {
            double w = barWidthMm ?? 1.0;
            _host = new AppHost();
            _host.Start();
            _host.SetBarWidthMm(w);
        };
        app.Exit += (_, _) => _host?.Dispose();
        app.Run();
    }
}
