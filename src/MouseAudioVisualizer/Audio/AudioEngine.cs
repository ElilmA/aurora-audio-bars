namespace MouseAudioVisualizer.Audio;

/// <summary>
/// 音频宿主：后台线程循环读取环形缓冲 → 频谱分析，
/// 通过 Current 暴露最新 SpectrumFrame 供渲染线程读取（volatile 引用交换）。
/// 采用 50% 重叠分析，帧率约 2× 窗口刷新率，提升平滑度。
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private readonly AudioCapture _capture;
    private readonly SpectrumEngine _spectrum;
    private readonly Thread _thread;
    private readonly float[] _analysis = new float[SpectrumEngine.FftSize];
    private readonly float[] _readBuf = new float[SpectrumEngine.FftSize];
    private volatile SpectrumFrame? _current;
    private volatile bool _running;

    public AudioCapture Capture => _capture;
    public SpectrumEngine Spectrum => _spectrum;
    public SpectrumFrame? Current => _current;

    public event Action<Exception>? Error;

    public AudioEngine()
    {
        _capture = new AudioCapture();
        _spectrum = new SpectrumEngine(_capture.SampleRate);
        _thread = new Thread(Loop) { IsBackground = true, Name = "AudioEngine" };
    }

    public void Start()
    {
        _running = true;
        _capture.Error += ex => Error?.Invoke(ex);
        _capture.Start();
        _thread.Start();
    }

    private void Loop()
    {
        // 每次新读取一半窗口（重叠 50%），分析最近 FftSize 个采样
        int step = SpectrumEngine.FftSize / 2;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long last = 0;
        const long frameIntervalMs = 21; // ~47fps，与窗口重叠分析速率匹配
        while (_running)
        {
            // 节流：避免数据充足时无限高速空转（保证 ≤ ~47fps 处理频率）
            long now = sw.ElapsedMilliseconds;
            long wait = last + frameIntervalMs - now;
            if (wait > 0)
            {
                Thread.Sleep((int)Math.Min(wait, 10));
                continue;
            }
            last = now;

            int got = _capture.Read(_readBuf, step);
            if (got < step)
            {
                Thread.Sleep(2);
                continue;
            }
            // 滚动：把已有分析窗口左移 step
            Array.Copy(_analysis, step, _analysis, 0, SpectrumEngine.FftSize - step);
            Array.Copy(_readBuf, 0, _analysis, SpectrumEngine.FftSize - step, step);
            _current = _spectrum.Analyze(_analysis);
        }
    }

    public void Dispose()
    {
        _running = false;
        _capture.Stop();
        _thread.Join(1000);
        _capture.Dispose();
    }
}
