using NAudio.Wave;

namespace MouseAudioVisualizer.Audio;

/// <summary>
/// 使用 NAudio WasapiLoopbackCapture 捕获系统输出音频(loopback)，
/// 将 PCM 数据写入内部环形缓冲，供频谱引擎按帧读取。
/// </summary>
public sealed class AudioCapture : IDisposable
{
    private readonly object _lock = new();
    private readonly float[] _ring;
    private readonly WasapiLoopbackCapture _capture;
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
        _ring = new float[ringCapacity];
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
    /// 从环形缓冲读取最近 count 个单声道采样（多声道取均值）。
    /// 返回实际读取的采样数（不足 count 时返回已有数量）。
    /// </summary>
    public int Read(float[] dest, int count)
    {
        lock (_lock)
        {
            int n = Math.Min(count, _count);
            int ch = Math.Max(1, Channels);
            for (int i = 0; i < n; i++)
            {
                int idx = (_writePos - n + i + _ring.Length) % _ring.Length;
                dest[i] = _ring[idx];
            }
            return n;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        int bytes = e.BytesRecorded;
        if (bytes < 4) return;

        int bits = _capture.WaveFormat.BitsPerSample;
        int ch = Math.Max(1, Channels);

        if (bits == 32)
        {
            int samples = bytes / 4;
            lock (_lock)
            {
                // 通道混为单声道
                for (int s = 0; s < samples; s += ch)
                {
                    float acc = 0f;
                    for (int c = 0; c < ch && s + c < samples; c++)
                    {
                        acc += BitConverter.ToSingle(e.Buffer, (s + c) * 4);
                    }
                    Push(acc / ch);
                }
            }
        }
        else if (bits == 16)
        {
            int samples = bytes / 2;
            lock (_lock)
            {
                for (int s = 0; s < samples; s += ch)
                {
                    float acc = 0f;
                    for (int c = 0; c < ch && s + c < samples; c++)
                    {
                        acc += BitConverter.ToInt16(e.Buffer, (s + c) * 2) / 32768f;
                    }
                    Push(acc / ch);
                }
            }
        }
        else
        {
            // 不支持的其他格式，忽略（loopback 常见为 32bit float 或 16bit PCM）
        }
    }

    private void Push(float sample)
    {
        _ring[_writePos] = sample;
        _writePos = (_writePos + 1) % _ring.Length;
        if (_count < _ring.Length) _count++;
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
