using System.Windows.Media.Imaging;
using MouseAudioVisualizer.Audio;

namespace MouseAudioVisualizer.Visual;

/// <summary>
/// 纵向能量条渲染器（Aurora 升级版）：
/// 「从屏幕底部向上生长的全色谱 Neon Aurora 能量流」。
///
/// - 颜色：完整 360° 色相沿高度连续流动（紫→蓝→青→绿→黄→橙→红→粉→紫），
///   无硬切色块；hue drift 以 12s/圈的极慢速度整体流动。
/// - 左右两侧通过 hueStart 给不同相位（左紫、右青），仍是同一完整色谱，视觉平衡。
/// - 主体：高饱和纵向连续渐变；x 方向上中间列更亮、边缘轻微衰减（内光晕）。
/// - 顶部：液态圆头——核心略上抬至 fillPx+尾晕，顶部 16px 光晕尾渐进消失，
///   近顶 12px 有轻微 bright bloom（跟随高度移动）。
/// - 呼吸感：亮度/饱和度随当前音量轻微变化（非闪烁）。
/// - 每帧绘制前整图清零 → 无残影，只有当前这一帧。
/// - 保真项：底部向上、连续、attack/release、高度=音量、60FPS 均保留。
/// </summary>
public sealed class EdgeRenderer
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _bandCount;
    private readonly float[] _rowPos;   // 行 → 高度比例 [0..1]（0=底部 1=顶部）
    private readonly float _hueStart;   // 起始色相（底部），左右不同
    private readonly long _t0;          // 用于 hue drift 计时

    private float _smoothH;     // 平滑后的填充高度（0..1）
    private readonly float[] _trailFrames = new float[TrailFrameCount]; // 有限历史高度环
    private int _trailIdx;
    private const int TrailFrameCount = 4;   // 只保留最近 4 帧（短暂视觉惯性）
    private const float TrailMaxRatio = 1.08f; // 拖影最多延伸到主体高度的 8%（短尾）

    public EdgeRenderer(int widthPx, int heightPx, int bandCount, float hueStart)
    {
        _width = widthPx;
        _height = heightPx;
        _bandCount = bandCount;
        _hueStart = hueStart;
        _t0 = Environment.TickCount64;
        _rowPos = new float[heightPx];
        for (int y = 0; y < heightPx; y++)
        {
            _rowPos[y] = (float)(heightPx - 1 - y) / Math.Max(1, heightPx - 1); // 0=底部 1=顶部
        }
    }

    /// <summary>将频谱帧绘制到竖条 bitmap（Pbgra32，尺寸与构造一致）。</summary>
    public void Draw(WriteableBitmap bitmap, SpectrumFrame frame, float intensity, float alpha)
    {
        int stride = _width * 4;
        int halfW = _width / 2;
        bitmap.Lock();
        unsafe
        {
            byte* px = (byte*)bitmap.BackBuffer.ToPointer();
            var levels = frame.Levels;

            // 1) 聚合全局音量（RMS + Peak）
            float vol = AggregateVolume(levels, frame.Peak) * intensity;
            vol = MathF.Min(1.25f, vol);

            // 2) 目标高度映射：静音~3%，小音量~15-25%，正常~40-60%，大声~80-100%
            float targetH = MathF.Min(1f, 0.03f + vol * 0.95f);

            // 3) attack / release 平滑：目标实时跟随
            //    attack ≈ 30-60ms（k=0.7@16ms 帧 → 约 33ms 达 90%）
            //    release ≈ 50-120ms（k=0.28@16ms 帧 → 约 100ms 达 90%）
            //    主体必须「几乎立即」响应，拖影承担视觉惯性。
            float k = targetH > _smoothH ? 0.72f : 0.30f;
            _smoothH += (targetH - _smoothH) * k;
            if (_smoothH > 1f) _smoothH = 1f;
            if (_smoothH < 0f) _smoothH = 0f;

            // 4) 每帧先清空整张 bitmap（防残影：上一帧必须完全消失）
            for (int y = 0; y < _height; y++)
            {
                byte* row = px + y * stride;
                for (int x = 0; x < _width; x++)
                {
                    row[x * 4] = 0;
                    row[x * 4 + 1] = 0;
                    row[x * 4 + 2] = 0;
                    row[x * 4 + 3] = 0;
                }
            }

            float fillPx = _smoothH * _height;

            // 4a) 有限生命周期 trail：把当前高度写入历史环（入队后取最近 4 帧）
            _trailFrames[_trailIdx] = _smoothH;
            _trailIdx = (_trailIdx + 1) % TrailFrameCount;

            // 5) 色谱流动相位：12 秒走完一整圈（很慢，不像彩灯）
            float drift = ((Environment.TickCount64 - _t0) % 12000L) / 12000f * 360f;

            // 6) 呼吸感：亮度/饱和随音量微调（跟随音频，非闪烁）
            float s = 0.92f + 0.08f * MathF.Min(1f, vol);
            float v = 0.84f + 0.16f * MathF.Min(1f, vol);

            const float glowTail = 16f;   // 顶部光晕尾（超过 fillPx 向上）
            const float bloomPx = 12f;    // 近顶 bright bloom 区

            // 7a) 先绘制 trail 层（历史高度带，低 alpha，快速衰减）：
            //     只画历史高度高于当前主体之上的部分，且限制不超过主体高度的 8%，
            //     短尾、柔、透明；主体随后覆盖。
            //     历史帧只存在最近 4 帧 → 主体下降后旧高位自然回收，无永久残留。
            for (int i = 0; i < TrailFrameCount; i++)
            {
                float histH = _trailFrames[(_trailIdx + TrailFrameCount - 1 - i) % TrailFrameCount];
                // 最新帧最近（i=0 -> alpha 高），越旧越淡
                float trailAlphaMul = i switch { 0 => 0.16f, 1 => 0.11f, 2 => 0.07f, _ => 0.04f };

                float histPx = histH * _height;
                if (histPx <= fillPx + 0.5f) continue;             // 低于主体 → 被主体覆盖，省略

                // 拖影上界 clamp：不超过主体高度的 TrailMaxRatio（短尾，不长）
                float trailTop = MathF.Min(histPx, fillPx * TrailMaxRatio + 1f) + 6f;

                for (int y = 0; y < _height; y++)
                {
                    float rowFromBottom = _height - 1 - y;
                    if (rowFromBottom <= fillPx) continue;         // 主体区交给主体绘制
                    if (rowFromBottom > trailTop) continue;

                    float pos = _rowPos[y];
                    float hue = _hueStart - pos * 360f + drift;
                    hue %= 360f;
                    if (hue < 0f) hue += 360f;
                    HsvToRgb(hue, s, v, out byte r, out byte g, out byte b);

                    float t = (trailTop - rowFromBottom) / 6f;      // 顶缘柔和淡出
                    float a = alpha * trailAlphaMul * Math.Clamp(t, 0f, 1f);
                    if (a <= 0.003f) continue;

                    byte* row = px + y * stride;
                    int halfWl = halfW;
                    for (int x = 0; x < _width; x++)
                    {
                        int dx = Math.Abs(x - halfWl);
                        float edge = dx <= 0 ? 1f : 0.88f + 0.12f * (1f - (float)dx / Math.Max(1f, halfWl));
                        int o = x * 4;
                        row[o] = (byte)(b * 0.7 * edge);
                        row[o + 1] = (byte)(g * 0.7 * edge);
                        row[o + 2] = (byte)(r * 0.7 * edge);
                        row[o + 3] = (byte)(a * 255);
                    }
                }
            }

            // 7b) 绘制主体：核心 0→fillPx + 顶部液态圆头（bloom + 光晕尾）
            for (int y = 0; y < _height; y++)
            {
                float rowFromBottom = _height - 1 - y;
                float top = fillPx + glowTail;                 // 含光晕尾的上界
                if (rowFromBottom > top) continue;

                float pos = _rowPos[y];

                // 全色谱 hue：紫→蓝→青→绿→黄→橙→红→粉→紫（一整圈连续）
                float hue = _hueStart - pos * 360f + drift;
                hue %= 360f;
                if (hue < 0f) hue += 360f;
                HsvToRgb(hue, s, v, out byte r, out byte g, out byte b);

                // 顶部形态：核心区 / bloom 隆起 / 光晕尾
                float aScale;  // 该行整体 alpha 系数（0..1+）
                float bright = 1f;
                if (rowFromBottom >= fillPx)
                {
                    // 光晕尾：>fillPx 的渐变消失（液态顶端呼吸）
                    float t = (top - rowFromBottom) / glowTail;       // 1→0
                    aScale = 0.55f * t * t;                            // 平方衰减，柔和
                }
                else
                {
                    // 核心区全亮；近顶轻轻 bloom 隆起（发光圆头）
                    aScale = 1f;
                    float distTop = fillPx - rowFromBottom;            // 距填充顶
                    if (distTop < bloomPx)
                    {
                        float t = 1f - distTop / bloomPx;              // 0→1
                        bright = 1f + 0.14f * t * (1f - t) * 4f;       // 中部微微隆起
                    }
                }

                float a = alpha * Math.Clamp(aScale, 0f, 1f);
                if (a <= 0.003f) continue;

                byte rr = (byte)MathF.Min(255f, r * bright);
                byte gg = (byte)MathF.Min(255f, g * bright);
                byte bb = (byte)MathF.Min(255f, b * bright);
                byte aa = (byte)(a * 255);

                byte* row = px + y * stride;
                for (int x = 0; x < _width; x++)
                {
                    // x 方向：中心列更亮、边缘轻微衰减（内部光晕感）
                    int dx = Math.Abs(x - halfW);
                    float edge = dx <= 0 ? 1f : 0.88f + 0.12f * (1f - (float)dx / Math.Max(1f, halfW));
                    int o = x * 4;
                    row[o] = (byte)(bb * edge);
                    row[o + 1] = (byte)(gg * edge);
                    row[o + 2] = (byte)(rr * edge);
                    row[o + 3] = aa;
                }
            }
        }
        bitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, _width, _height));
        bitmap.Unlock();
    }

    /// <summary>全局音量：各频带能量平方均值（RMS 近似）+ 峰值加权。</summary>
    private static float AggregateVolume(float[] levels, float peak)
    {
        if (levels.Length == 0) return peak;
        double sumSq = 0;
        foreach (var l in levels) sumSq += (double)l * l;
        float rms = (float)Math.Sqrt(sumSq / levels.Length);
        // 峰值反映瞬态，RMS 反映持续能量；权重让打击感与持续音都可见
        return MathF.Min(1.2f, 0.72f * rms * 2.2f + 0.28f * peak);
    }

    private static void HsvToRgb(float h, float s, float v, out byte r, out byte g, out byte b)
    {
        h %= 360f;
        if (h < 0f) h += 360f;
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f % 2f) - 1f));
        float m = v - c;
        float r0, g0, b0;
        if (h < 60) { r0 = c; g0 = x; b0 = 0; }
        else if (h < 120) { r0 = x; g0 = c; b0 = 0; }
        else if (h < 180) { r0 = 0; g0 = c; b0 = x; }
        else if (h < 240) { r0 = 0; g0 = x; b0 = c; }
        else if (h < 300) { r0 = x; g0 = 0; b0 = c; }
        else { r0 = c; g0 = 0; b0 = x; }
        r = (byte)((r0 + m) * 255);
        g = (byte)((g0 + m) * 255);
        b = (byte)((b0 + m) * 255);
    }
}