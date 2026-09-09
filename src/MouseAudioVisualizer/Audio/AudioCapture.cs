using NAudio.Wave;

namespace MouseAudioVisualizer.Audio;

/// <summary>
/// 使用 NAudio WasapiLoopbackCapture 捕获系统输出音频(loopback)，
/// 将 PCM 数据按左右声道解交织写入两条环形缓冲（偶索引声道→左、奇索引声道→右；
/// 立体声即原生 L/R；单声道左右镜像相同；多声道近似下混）。
/// 读侧提供 ReadLR（分声道读取）与 Read（左右均值 = 原复合单声道行为）。
/// </summary>
public sealed class AudioCapture : IDisposable
{
    private readonly object _lock = new();
    private readonly float[] _ringL;
    private readonly float[] _ringR;
    private readonly WasapiLoopbackCapture _capture;
    private float[] _scratch = Array.Empty<float>();
    private int _writePos;
    private int _count;

    public int SampleRate { get; }
    public int Channels { get; }
    public string DeviceName { get; }

    public event Action<Exception>? Error;

    public AudioCapture(int ringCapacity = 65536)
    {
        _capture = new WasapiLoopbackCapture();
        var wf = _capture.WaveFormat;
        SampleRate = wf.SampleRate;
        Channels = wf.Channels;
        DeviceName = _capture.GetType().Name;
        _ringL = new float[ringCapacity];
        _ringR = new float[ringCapacity];
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public void Start()
    {
        _capture.StartRecording();
    }

    public void Stop()
    {
        _capture.StopRecording();
    }

    /// <summary>
    /// 读取最近 count 个「左右均值」单声道采样（兼容原复合模式 / 验证工具）。
    /// 返回实际读取的采样数。
    /// </summary>
    public int Read(float[] dest, int count)
    {
        lock (_lock)
        {
            int n = Math.Min(count, _count);
            if (_scratch.Length < n) _scratch = new float[Math.Max(64, n)];
            ReadFrom(_ringL, dest, n);
            ReadFrom(_ringR, _scratch, n);
            for (int i = 0; i < n; i++) dest[i] = (dest[i] + _scratch[i]) * 0.5f;
            return n;
        }
    }

    /// <summary>
    /// 读取最近 count 个左声道采样到 leftDest、右声道采样到 rightDest。
    /// 返回实际读取的采样数（左右缓冲写入数量一致）。
    /// </summary>
    public int ReadLR(float[] leftDest, float[] rightDest, int count)
    {
        lock (_lock)
        {
            int n = Math.Min(count, _count);
            ReadFrom(_ringL, leftDest, n);
            ReadFrom(_ringR, rightDest, n);
            return n;
        }
    }

    /// <summary>把环形缓冲最近的 n 个采样按时间顺序写入 dest[0..n)。</summary>
    private void ReadFrom(float[] ring, float[] dest, int n)
    {
        for (int i = 0; i < n; i++)
        {
            dest[i] = ring[(_writePos - n + i + ring.Length) % ring.Length];
        }
    }

    private void Push(float left, float right)
    {
        _ringL[_writePos] = left;
        _ringR[_writePos] = right;
        _writePos = (_writePos + 1) % _ringL.Length;
        if (_count < _ringL.Length) _count++;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        int bytes = e.BytesRecorded;
        if (bytes < 4) return;

        int bits = _capture.WaveFormat.BitsPerSample;
        int ch = Math.Max(1, Channels);

        if (bits == 32)
        {
            int frames = bytes / (4 * ch);
            if (frames == 0) return;
            lock (_lock)
            {
                for (int f = 0; f < frames; f++)
                {
                    float l = 0f, r = 0f;
                    int lc = 0, rc = 0;
                    for (int c = 0; c < ch; c++)
                    {
                        float v = BitConverter.ToSingle(e.Buffer, (f * ch + c) * 4);
                        if ((c & 1) == 0) { l += v; lc++; }
                        else { r += v; rc++; }
                    }
                    float pl = lc > 0 ? l / lc : 0f;
                    float pr = rc > 0 ? r / rc : pl; // 单声道：右镜像左
                    Push(pl, pr);
                }
            }
        }
        else if (bits == 16)
        {
            int frames = bytes / (2 * ch);
            if (frames == 0) return;
            lock (_lock)
            {
                for (int f = 0; f < frames; f++)
                {
                    float l = 0f, r = 0f;
                    int lc = 0, rc = 0;
                    for (int c = 0; c < ch; c++)
                    {
                        float v = BitConverter.ToInt16(e.Buffer, (f * ch + c) * 2) / 32768f;
                        if ((c & 1) == 0) { l += v; lc++; }
                        else { r += v; rc++; }
                    }
                    float pl = lc > 0 ? l / lc : 0f;
                    float pr = rc > 0 ? r / rc : pl;
                    Push(pl, pr);
                }
            }
        }
        else
        {
            // 不支持的其他格式，忽略（loopback 常见为 32bit float 或 16bit PCM）
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Error?.Invoke(e.Exception);
        }
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
    }
}
