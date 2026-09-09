using System.Drawing;
using System.Windows.Forms;
using MouseAudioVisualizer.Visual;

namespace MouseAudioVisualizer.Shell;

/// <summary>托盘图标：开关、样式、强度、透明度、开机自启、退出。</summary>
public sealed class TrayIcon : IDisposable
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "MouseAudioVisualizer";

    private readonly NotifyIcon _icon;
    private readonly AppHost _host;

    public TrayIcon(AppHost host)
    {
        _host = host;
        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Mouse Audio Visualizer",
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        var toggle = new ToolStripMenuItem("启用/禁用") { Checked = _host.IsEnabled };
        toggle.Click += (_, _) =>
        {
            _host.Toggle();
            toggle.Checked = _host.IsEnabled;
        };

        var intensity = new ToolStripMenuItem("强度");
        foreach (var (label, value) in new[] { ("低 0.6x", 0.6f), ("中 1.0x", 1.0f), ("高 1.6x", 1.6f) })
        {
            var item = new ToolStripMenuItem(label) { Checked = Math.Abs(value - 1.0f) < 0.01f };
            float v = value;
            item.Click += (_, _) =>
            {
                _host.SetIntensity(v);
                foreach (ToolStripMenuItem s in intensity.DropDownItems) s.Checked = false;
                item.Checked = true;
            };
            intensity.DropDownItems.Add(item);
        }

        var opacity = new ToolStripMenuItem("透明度");
        foreach (var (label, value) in new[] { ("90%", 0.9f), ("70%", 0.7f), ("50%", 0.5f), ("30%", 0.3f) })
        {
            var item = new ToolStripMenuItem(label) { Checked = Math.Abs(value - 0.9f) < 0.01f };
            float v = value;
            item.Click += (_, _) =>
            {
                _host.SetOpacity(v);
                foreach (ToolStripMenuItem s in opacity.DropDownItems) s.Checked = false;
                item.Checked = true;
            };
            opacity.DropDownItems.Add(item);
        }

        var channelSplit = new ToolStripMenuItem("声道分离 (左=左声道/右=右声道)") { Checked = _host.ChannelSplit };
        channelSplit.Click += (_, _) =>
        {
            _host.ChannelSplit = !channelSplit.Checked;
            channelSplit.Checked = _host.ChannelSplit;
        };

        var autoStart = new ToolStripMenuItem("开机自启") { Checked = IsAutoStartEnabled };
        autoStart.Click += (_, _) =>
        {
            _host.SetAutoStart(!autoStart.Checked);
            autoStart.Checked = _host.IsAutoStartEnabled;
        };

        var width = new ToolStripMenuItem("条宽");
        foreach (var (label, mm) in new[] { ("1mm", 1.0), ("2mm", 2.0), ("3mm", 3.0), ("5mm", 5.0), ("8mm", 8.0) })
        {
            var item = new ToolStripMenuItem(label) { Checked = Math.Abs(mm - _host.BarWidthMm) < 0.01 };
            double v = mm;
            item.Click += (_, _) =>
            {
                _host.SetBarWidthMm(v);
                foreach (ToolStripMenuItem s in width.DropDownItems) s.Checked = false;
                item.Checked = true;
            };
            width.DropDownItems.Add(item);
        }

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        menu.Items.Add(toggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(intensity);
        menu.Items.Add(opacity);
        menu.Items.Add(channelSplit);
        menu.Items.Add(width);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(autoStart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        _icon.ContextMenuStrip = menu;
    }

    /// <summary>读取开机自启状态。</summary>
    public static bool IsAutoStartEnabled
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(RunValueName) != null;
            }
            catch { return false; }
        }
    }

    /// <summary>设置/取消开机自启（HKCU Run 键，指向当前 exe 路径）。</summary>
    public static void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                {
                    key.SetValue(RunValueName, $"\"{exe}\"");
                }
            }
            else
            {
                key.DeleteValue(RunValueName, false);
            }
        }
        catch { }
    }

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var pen = new Pen(Color.FromArgb(80, 180, 255), 2f);
            g.DrawEllipse(pen, 2, 2, 11, 11);
            using var fill = new SolidBrush(Color.FromArgb(80, 180, 255));
            g.FillEllipse(fill, 5, 5, 5, 5);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}