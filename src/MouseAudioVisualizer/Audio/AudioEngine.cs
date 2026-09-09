namespace MouseAudioVisualizer.Audio;

/// <summary>
/// 音频宿主：后台线程循环读取环形缓冲（左右声道）→ 频谱分析。
///
/// 两种模式（可托盘实时切换）：
/// - 声道分离关闭（默认）：左右采样均值合成为单一频谱，左右条显示相同内容（原行为）。
/// - 声道分离开启：左声道、右声道各自独立 FFT/AGC/平滑，左条=左声道、右条=右声道。
///
/// 通过 Left / Right 暴露最新频谱帧供渲染线程读取（volatile 引用交换）。
/// 采用 50% 重叠分析，帧率约 2× 窗口刷新率，提升平滑度。
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private readonly AudioCapture _capture;
    private readonly SpectrumEngine _spectrumMono; // 复合（左右均值）
    private readonly SpectrumEngine _spectrumL;    // 左声道
    private readonly SpectrumEngine _spectrumR;    // 右声道
    private readonly Thread _thread;

    private readonly float[] _readL = new float[SpectrumEngine.FftSize];
    private readonly float[] _readR = new float[SpectrumEngine.FftSize];
    private readonly float[] _analysisMono = new float[SpectrumEngine.FftSize];
    private readonly float[] _analysisL = new float[SpectrumEngine.FftSize];
    private readonly float[] _analysisR = new float[SpectrumEngine.FftSize];

    private volatile SpectrumFrame? _frameMono;
    private volatile SpectrumFrame? _frameL;
    private volatile SpectrumFrame? _frameR;
    private volatile bool _running;
    private volatile bool _channelSplit;

    public AudioCapture Capture => _capture;
    public bool ChannelSplit { get => _channelSplit; set => _channelSplit = value; }

    /// <summary>左条使用的帧：分离=左声道；复合=左右均值。</summary>
    public SpectrumFrame? Left => _channelSplit ? _frameL : _frameMono;
    /// <summary>右条使用的帧：分离=右声道；复合=左右均值。</summary>
    public SpectrumFrame? Right => _channelSplit ? _frameR : _frameMono;

    public event Action<Exception>? Error;

    public AudioEngine()
    {
        _capture = new AudioCapture();
        int sr = _capture.SampleRate;
        _spectrumMono = new SpectrumEngine(sr);
        _spectrumL = new SpectrumEngine(sr);
        _spectrumR = new SpectrumEngine(sr);
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

            int got = _capture.ReadLR(_readL, _readR, step);
            if (got < step)
            {
                Thread.Sleep(2);
                continue;
            }

            if (_channelSplit)
            {
                // 分离模式：左右各独立滚动分析窗口 + 独立 FFT/AGC/平滑
                Roll(_analysisL, _readL, step);
                Roll(_analysisR, _readR, step);
                _frameL = _spectrumL.Analyze(_analysisL);
                _frameR = _spectrumR.Analyze(_analysisR);
            }
            else
            {
                // 复合模式：左右均值合成单声道分析窗口
                Roll(_analysisMono, _readL, _readR, step);
                _frameMono = _spectrumMono.Analyze(_analysisMono);
            }
        }
    }

    /// <summary>滚动窗口：左移 step，把新读入的 step 个采样放到尾部。</summary>
    private static void Roll(float[] analysis, float[] read, int step)
    {
        int size = SpectrumEngine.FftSize;
        Array.Copy(analysis, step, analysis, 0, size - step);
        Array.Copy(read, 0, analysis, size - step, step);
    }

    /// <summary>滚动窗口（复合）：尾部写入左右均值。</summary>
    private static void Roll(float[] analysis, float[] readL, float[] readR, int step)
    {
        int size = SpectrumEngine.FftSize;
        Array.Copy(analysis, step, analysis, 0, size - step);
        for (int i = 0; i < step; i++)
        {
            analysis[size - step + i] = (readL[i] + readR[i]) * 0.5f;
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
