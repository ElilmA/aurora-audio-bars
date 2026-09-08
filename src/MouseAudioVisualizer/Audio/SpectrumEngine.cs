using NAudio.Dsp;

namespace MouseAudioVisualizer.Audio;

/// <summary>
/// PCM → FFT → 对数频带能量，带 attack/decay 平滑。
/// 线程安全：Analyze 由后台线程调用，仅更新内部缓冲。
/// </summary>
public sealed class SpectrumEngine
{
    public const int FftSize = 2048;
    public const int BandCount = 48;

    private readonly int _sampleRate;
    private readonly float[] _window;
    private readonly Complex[] _fft;
    private readonly float[] _mag;      // 每 bin 幅度
    private readonly int[] _bandStart;  // 每 band 起始 bin
    private readonly int[] _bandEnd;    // 每 band 结束 bin(不含)
    private readonly float[] _smooth;   // 平滑后的 band 能量
    private readonly float[] _levels;   // 输出 0..1

    private float _attack = 0.6f;     // 上升系数(越大越快)
    private float _decay = 0.35f;     // 下降系数(越大越快，降滞后)
    private float _gain = 1.0f;      // 幅度增益
    private float _agcLevel = 0f;    // AGC 估计电平
    private bool _agcEnabled = true;
    private float _agcTarget = 0.75f; // AGC 目标峰值
    private float _agcMin = 0.5f;
    private float _agcMax = 40f;

    public int SampleRate => _sampleRate;
    public int BandCountActual => BandCount;

    public float Attack { get => _attack; set => _attack = Math.Clamp(value, 0.01f, 1f); }
    public float Decay { get => _decay; set => _decay = Math.Clamp(value, 0.005f, 1f); }
    public float Gain { get => _gain; set => _gain = Math.Clamp(value, 0.1f, 100f); }
    public bool AgcEnabled { get => _agcEnabled; set => _agcEnabled = value; }
    public float AgcTarget { get => _agcTarget; set => _agcTarget = Math.Clamp(value, 0.05f, 1f); }

    public SpectrumEngine(int sampleRate)
    {
        _sampleRate = sampleRate;
        _window = BuildHann(FftSize);
        _fft = new Complex[FftSize];
        _mag = new float[FftSize / 2];
        _smooth = new float[BandCount];
        _levels = new float[BandCount];
        (_bandStart, _bandEnd) = BuildLogBands(FftSize, sampleRate, 20f, 20000f, BandCount);
    }

    /// <summary>
    /// 对最近 samples.Length 个采样（建议正好 FftSize）做加窗 FFT，
    /// 更新频带能量与平滑。返回可复用的输出引用（勿修改）。
    /// </summary>
    public SpectrumFrame Analyze(float[] samples)
    {
        int n = Math.Min(FftSize, samples.Length);
        for (int i = 0; i < n; i++)
        {
            _fft[i].X = samples[i] * _window[i];
            _fft[i].Y = 0f;
        }
        for (int i = n; i < FftSize; i++)
        {
            _fft[i].X = 0f;
            _fft[i].Y = 0f;
        }

        FastFourierTransform.FFT(true, (int)Math.Log2(FftSize), _fft);

        int half = FftSize / 2;
        float maxMag = 0f;
        for (int i = 0; i < half; i++)
        {
            float x = _fft[i].X, y = _fft[i].Y;
            // NAudio FFT 输出已按 N 归一化：单位幅度正弦波峰值约 0.5，无需再除以 half
            float m = MathF.Sqrt(x * x + y * y);
            _mag[i] = m;
            if (m > maxMag) maxMag = m;
        }

        // 简单 AGC：跟踪慢速峰值，把整体电平映射到目标峰值附近
        float gain = _gain;
        if (_agcEnabled)
        {
            float attack = maxMag > _agcLevel ? 0.12f : 0.05f;   // AGC 快速跟随(防旧增益滞后)
            _agcLevel += (maxMag - _agcLevel) * attack;
            if (_agcLevel > 0.0001f)
            {
                gain = _gain * Math.Clamp(_agcTarget / _agcLevel, _agcMin, _agcMax);
            }
        }

        for (int b = 0; b < BandCount; b++)
        {
            int start = _bandStart[b], end = _bandEnd[b];
            float peak = 0f;
            for (int i = start; i < end; i++)
            {
                if (_mag[i] > peak) peak = _mag[i];
            }
            float target = MathF.Min(1f, peak * gain);
            float coeff = target > _smooth[b] ? _attack : _decay;
            _smooth[b] += (target - _smooth[b]) * coeff;
            _levels[b] = _smooth[b];
        }

        float peakLevel = 0f;
        for (int b = 0; b < BandCount; b++)
        {
            if (_levels[b] > peakLevel) peakLevel = _levels[b];
        }

        return new SpectrumFrame
        {
            Levels = _levels,
            Peak = peakLevel,
            TimestampMs = Environment.TickCount64,
        };
    }

    private static float[] BuildHann(int n)
    {
        var w = new float[n];
        for (int i = 0; i < n; i++)
        {
            w[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (n - 1)));
        }
        return w;
    }

    /// <summary>对数频率分布频带，返回 [startBin, endBin) 数组。</summary>
    private static (int[] start, int[] end) BuildLogBands(int fftSize, int sampleRate, float fMin, float fMax, int bands)
    {
        var start = new int[bands];
        var end = new int[bands];
        double nyquist = sampleRate / 2.0;
        fMax = Math.Min(fMax, (float)nyquist * 0.95f);
        double ratio = fMax / fMin;
        for (int b = 0; b < bands; b++)
        {
            double f0 = fMin * Math.Pow(ratio, (double)b / bands);
            double f1 = fMin * Math.Pow(ratio, (double)(b + 1) / bands);
            int i0 = (int)(f0 * fftSize / sampleRate);
            int i1 = (int)(f1 * fftSize / sampleRate);
            if (i1 <= i0) i1 = i0 + 1;
            if (i1 > fftSize / 2) i1 = fftSize / 2;
            start[b] = i0;
            end[b] = i1;
        }
        return (start, end);
    }
}
