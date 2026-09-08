namespace MouseAudioVisualizer.Audio;

/// <summary>
/// 一次频谱分析的结果帧（不可变，供渲染线程读取）。
/// </summary>
public sealed class SpectrumFrame
{
    /// <summary>各频带归一化能量，范围 0..1。</summary>
    public float[] Levels { get; init; } = Array.Empty<float>();

    /// <summary>全局峰值（0..1），用于辉光/中心光点。</summary>
    public float Peak { get; init; }

    /// <summary>帧时间戳（ms），用于平滑调试。</summary>
    public long TimestampMs { get; init; }
}
