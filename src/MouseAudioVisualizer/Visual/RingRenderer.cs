using System.Windows.Media.Imaging;
using MouseAudioVisualizer.Audio;

namespace MouseAudioVisualizer.Visual;

/// <summary>渲染样式。</summary>
public enum VisualStyle
{
    /// <summary>环形频谱（默认）：频带按角度分布，能量越高越向外。</summary>
    Ring = 0,

    /// <summary>波纹：从中心向外扩散的同心圆环，半径随整体能量波动。</summary>
    Ripple = 1,
}

/// <summary>
/// 极坐标环形频谱渲染器：将频带能量绘制为以中心为原点的放射状弧条。
/// 使用 WriteableBitmap 直接写像素，透明背景，每帧全量重绘。
/// 性能：构造时预计算每个像素的距离与频带索引查找表，帧内零三角函数。
/// </summary>
public sealed class RingRenderer
{
    private readonly int _size;
    private readonly int _cx, _cy;
    private readonly float _innerRadius;
    private readonly float _outerRadius;
    private readonly int _bandCount;
    private readonly byte[] _colorCache;       // band → BGR color (b,g,r)

    // 预计算查找表（每像素）：0=透明区, 1..bandCount=对应频带, -1=中心核心区
    private readonly short[] _bandMap;
    private readonly float[] _distMap;
    private readonly int[] _activePixels;  // 有效像素（环+核心）的平面索引，避免遍历全图

    public RingRenderer(int size, int bandCount, float innerRadiusRatio = 0.35f, float outerRadiusRatio = 0.48f)
    {
        _size = size;
        _bandCount = bandCount;
        _cx = size / 2;
        _cy = size / 2;
        _innerRadius = size * innerRadiusRatio;
        _outerRadius = size * outerRadiusRatio;
        _colorCache = new byte[bandCount * 3];
        for (int b = 0; b < bandCount; b++)
        {
            // 色相从低频(蓝/青)到高频(紫/粉)平滑过渡
            HsvToRgb((float)b / bandCount * 300f, 0.85f, 1f,
                out _colorCache[b * 3 + 2], out _colorCache[b * 3 + 1], out _colorCache[b * 3]);
        }

        _bandMap = new short[size * size];
        _distMap = new float[size * size];
        var active = new List<int>(size * size / 3);
        BuildLookupTable(active);
        _activePixels = active.ToArray();
    }

    private void BuildLookupTable(List<int> active)
    {
        float coreR = _innerRadius * 0.9f;
        for (int y = 0; y < _size; y++)
        {
            float dy = y - _cy;
            for (int x = 0; x < _size; x++)
            {
                float dx = x - _cx;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                int idx = y * _size + x;
                _distMap[idx] = dist;
                if (dist < coreR)
                {
                    _bandMap[idx] = -1; // 中心核心区
                    active.Add(idx);
                }
                else if (dist >= _innerRadius - 1f && dist <= _outerRadius + 2f)
                {
                    _bandMap[idx] = (short)(BandIndex(MathF.Atan2(dy, dx)) + 1);
                    active.Add(idx);
                }
                else
                {
                    _bandMap[idx] = 0; // 透明
                }
            }
        }
    }

    /// <summary>将频谱帧绘制到 bitmap。bitmap 必须为 Pbgra32、与构造 size 一致。</summary>
    public void Draw(WriteableBitmap bitmap, SpectrumFrame frame, float intensity, float alpha, VisualStyle style = VisualStyle.Ring)
    {
        if (style == VisualStyle.Ripple)
        {
            DrawRipple(bitmap, frame, intensity, alpha);
            return;
        }

        int stride = _size * 4;
        bitmap.Lock();
        unsafe
        {
            byte* px = (byte*)bitmap.BackBuffer.ToPointer();
            var levels = frame.Levels;
            float peak = frame.Peak;
            float coreR = _innerRadius * 0.9f;

            // 上一帧内容仅可能存在于 active 像素，先清透明
            for (int i = 0; i < _activePixels.Length; i++)
            {
                int idx = _activePixels[i];
                px[idx * 4 + 3] = 0;
            }

            // 绘制：仅遍历 active 像素（环 + 核心）
            for (int i = 0; i < _activePixels.Length; i++)
            {
                int idx = _activePixels[i];
                short bandCode = _bandMap[idx];
                float dist = _distMap[idx];

                if (bandCode == -1)
                {
                    // 中心核心区：随 peak 亮起
                    if (peak <= 0.01f) continue;
                    float coreA = MathF.Min(1f, peak * 1.4f) * alpha;
                    float edge = 1f - (dist / coreR);
                    if (edge <= 0.02f) continue;
                    float coreAlpha = edge * coreA;
                    int coreOffset = idx * 4;
                    px[coreOffset] = 255;
                    px[coreOffset + 1] = 220;
                    px[coreOffset + 2] = 180;
                    px[coreOffset + 3] = (byte)(coreAlpha * 255);
                    continue;
                }

                int band = bandCode - 1;
                float level = band < levels.Length ? levels[band] : 0f;
                if (level <= 0.001f) continue;

                float rel = (dist - _innerRadius) / (_outerRadius - _innerRadius);
                if (rel > level * intensity) continue; // 频带能量越高越向外

                float falloff = 1f - rel;
                float lv = MathF.Min(1f, level * intensity);
                float brightness = 0.35f + 0.65f * lv;
                float a = MathF.Min(1f, (0.55f + 0.45f * falloff) * lv) * alpha;

                byte r = (byte)(_colorCache[band * 3 + 2] * brightness);
                byte g = (byte)(_colorCache[band * 3 + 1] * brightness);
                byte b = (byte)(_colorCache[band * 3] * brightness);
                byte aa = (byte)(a * 255);

                int o = idx * 4;
                px[o] = b;
                px[o + 1] = g;
                px[o + 2] = r;
                px[o + 3] = aa;
            }
        }
        bitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, _size, _size));
        bitmap.Unlock();
    }

    private int BandIndex(float angle)
    {
        // atan2 范围 [-π, π]，映射到 [0, 2π)
        float a = angle + MathF.PI;
        float t = a / (MathF.PI * 2f);
        int idx = (int)(t * _bandCount);
        if (idx >= _bandCount) idx = _bandCount - 1;
        if (idx < 0) idx = 0;
        return idx;
    }

    /// <summary>波纹样式：从中心向外扩散的同心圆环，半径随峰值波动，颜色由频带能量决定。</summary>
    private void DrawRipple(WriteableBitmap bitmap, SpectrumFrame frame, float intensity, float alpha)
    {
        int stride = _size * 4;
        bitmap.Lock();
        unsafe
        {
            byte* px = (byte*)bitmap.BackBuffer.ToPointer();
            var levels = frame.Levels;
            float peak = frame.Peak;
            float coreR = _innerRadius * 0.9f;

            for (int i = 0; i < _activePixels.Length; i++)
            {
                int idx = _activePixels[i];
                px[idx * 4 + 3] = 0;
            }

            if (peak <= 0.01f) { bitmap.Unlock(); return; }

            // 波纹半径：随 peak 从内向外扩张
            float rippleMax = _outerRadius * 0.95f;
            float rippleR = _innerRadius + (rippleMax - _innerRadius) * MathF.Min(1f, peak * intensity);

            for (int i = 0; i < _activePixels.Length; i++)
            {
                int idx = _activePixels[i];
                float dist = _distMap[idx];

                // 波纹亮带：集中在 rippleR 附近
                float bandWidth = 14f;
                float rel = MathF.Abs(dist - rippleR) / bandWidth;
                if (rel > 1f) continue;

                float falloff = 1f - rel; // 1=亮带中心
                // 频带能量决定该角度的波纹强度
                short bandCode = _bandMap[idx];
                float level;
                if (bandCode == -1)
                {
                    level = peak;
                }
                else
                {
                    int band = bandCode - 1;
                    level = band < levels.Length ? levels[band] : 0f;
                }

                float lv = MathF.Min(1f, level * intensity);
                if (lv <= 0.001f) continue;

                float brightness = 0.35f + 0.65f * lv;
                float a = MathF.Min(1f, falloff * lv * 1.4f) * alpha;

                int bandIdx = bandCode > 0 ? bandCode - 1 : (int)(peak * (_bandCount - 1));
                byte r = (byte)(_colorCache[bandIdx * 3 + 2] * brightness);
                byte g = (byte)(_colorCache[bandIdx * 3 + 1] * brightness);
                byte b = (byte)(_colorCache[bandIdx * 3] * brightness);
                byte aa = (byte)(a * 255);

                int o = idx * 4;
                px[o] = b;
                px[o + 1] = g;
                px[o + 2] = r;
                px[o + 3] = aa;
            }
        }
        bitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, _size, _size));
        bitmap.Unlock();
    }

    private static void HsvToRgb(float h, float s, float v, out byte r, out byte g, out byte b)
    {
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