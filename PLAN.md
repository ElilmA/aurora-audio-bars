# Mouse Audio Visualizer — 方案计划

> 目标:捕获系统音频,将频谱可视化渲染为跟随鼠标指针的悬浮效果(环形频谱/波纹),透明置顶、点击穿透,不干扰正常操作。
> 文档日期:2026-09-08
> 检索工具:GitHub CLI (gh) 实际检索,非虚构。

---

## 1. 需求拆解

1. **音频捕获**:Windows 系统输出(全部应用声音),WASAPI Loopback。
2. **实时频谱**:PCM → FFT → 频带能量,低延迟(20~30ms/帧)。
3. **鼠标可视化**:渲染层跟随鼠标指针,以光标为中心做环形频谱/波纹/粒子效果。
4. **悬浮层**:透明、无边框、置顶、点击穿透(WS_EX_TRANSPARENT),多显示器 + DPI 感知。
5. **控制**:托盘菜单(开关、强度、样式、透明度),可选开机自启。

---

## 2. GitHub 开源调研(2026-09-08 实际检索)

### 2.1 检索结果(组合需求未找到完整现成项目)

| 项目 | 语言 | 许可证 | Star / 活跃度 | 功能匹配 | 结论 |
|---|---|---|---|---|---|
| [naudio/NAudio](https://github.com/naudio/NAudio) | C# | MIT | 6.2k / 2026-09 活跃 | WASAPI Loopback 捕获 + FFT,成熟库 | ✅ **核心库直接复用** |
| [bastibe/SoundCard](https://github.com/bastibe/SoundCard) | Python | BSD-3 | 763 / 2026-06 活跃 | 系统音频捕获(Python 路线) | ✅ 备选库 |
| [CorpseCode/system_audio_visualizer](https://github.com/CorpseCode/system_audio_visualizer) | C++/Flutter | MIT | 4 / 2025-12 | WASAPI + FFT 实现 | ⚠️ Flutter 插件,只作参考 |
| [big-mon/win-audio-visualizer](https://github.com/big-mon/win-audio-visualizer) | Python | MIT | 0 / **已归档** | WASAPI 可视化 | ❌ 已归档,仅参考 |
| [FEDOTOVB/CursorTrail](https://github.com/FEDOTOVB/CursorTrail) | C# | **无 LICENSE** | 0 / 2026-04 | 鼠标轨迹可视化(最接近"跟随鼠标"场景) | ⚠️ 无许可**不可复制代码**,参考架构 |
| [HappypsychoX/chromascope](https://github.com/HappypsychoX/chromascope) | C# | **无 LICENSE** | 0 / 2026-08 | WPF 系统音频可视化 | ⚠️ 无许可不可复制,参考 |
| [marcopixel/monstercat-visualizer](https://github.com/marcopixel/monstercat-visualizer) | Rainmeter | MIT | 961 | 桌面音频可视化 | ⚠️ Rainmeter 生态,不支持鼠标跟随,弃 |
| [CalcProgrammer1/KeyboardVisualizer](https://github.com/CalcProgrammer1/KeyboardVisualizer) | C++ | GPL-2.0 | 613 | 音频驱动 RGB 外设 | ❌ 专注 RGB 外设,弃 |

### 2.2 调研结论

- **组合检索为空**:`audio visualizer cursor`、`audio reactive cursor`、`audio visualizer mouse` 等关键词无高匹配仓库,即"系统音频 + 鼠标跟随可视化"没有现成完整项目。
- **可复用**:NAudio(MIT,成熟活跃)直接解决音频捕获 + FFT;Win32 透明置顶窗口是系统标准能力。
- **不可复制**:CursorTrail / chromascope 无 LICENSE,仅参考其分层架构(Control/Visual/Platform 分离)。
- **最终策略:成熟库 + 自研胶水**,核心自研代码量约 300~600 行,不重复造轮子。

---

## 3. 技术选型

| 决策点 | 选择 | 理由 |
|---|---|---|
| 平台 | Windows 10/11 | WASAPI Loopback 原生支持 |
| 语言/框架 | C# .NET 8 + WPF | 透明置顶窗口成熟、性能好、NAudio 原生支持、可单文件发布 |
| 音频捕获 | NAudio `WasapiLoopbackCapture`(48kHz / 32bit float) | 成熟库,MIT |
| FFT | NAudio FFT / `System.Numerics` 或 FFTW | 2048 点,Hann 窗,~23ms 帧率 |
| 渲染 | WPF + `WriteableBitmap`(或 SkiaSharp)自绘极坐标环形频谱 | 纯矢量绘制,低开销 |
| 鼠标跟踪 | `GetCursorPos` 轮询(60Hz)+ 窗口 `SetWindowPos` 跟随;必要时 `SetWindowsHookEx(WH_MOUSE_LL)` | 简单可靠 |
| 悬浮窗 | `WS_EX_TRANSPARENT` + `WS_EX_TOOLWINDOW` + `WS_EX_LAYERED`,`AllowsTransparency` | 点击穿透、不进 Alt-Tab |
| DPI | Per-Monitor DPI Aware + 多显示器坐标 | 高分屏不模糊、跨屏正确 |

> 备选路线(快速验证原型):Python + PyQt6 + SoundCard + NumPy,1~2 天出可跑原型;正式版仍用 C#。

---

## 4. 系统架构

```
┌───────────────────────── MouseAudioVisualizer ─────────────────────────┐
│  AudioCapture (NAudio WasapiLoopbackCapture)                           │
│      → 20ms 数据块 → RingBuffer                                        │
│  SpectrumEngine                                                         │
│      → Hann 窗 + FFT(2048) → 对数频带能量(如 48 band)                    │
│      → 平滑:attack 快 / decay 慢                                        │
│  VisualEngine (WPF 渲染线程)                                            │
│      → 极坐标环形频谱:频带→角度/半径/亮度,叠加辉光、粒子                    │
│  CursorLayer                                                           │
│      → GetCursorPos(60Hz) → SetWindowPos(窗口中心=光标)                  │
│  Shell (托盘菜单:开关/强度/样式/透明度/开机自启)                          │
└────────────────────────────────────────────────────────────────────────┘
```

**目录结构(仓库根 = `D:\系统\Documents\Desktop\mouse`)**:

```
mouse/
├── PLAN.md                 # 本方案
├── README.md               # 项目说明
├── .gitignore
└── src/
    └── MouseAudioVisualizer/
        ├── Program.cs              # 入口 + 应用宿主
        ├── Audio/AudioCapture.cs   # NAudio loopback 封装
        ├── Audio/SpectrumEngine.cs # FFT + 频带映射 + 平滑
        ├── Visual/CursorOverlay.cs # 透明置顶跟随窗口
        ├── Visual/RingRenderer.cs  # 环形频谱绘制
        └── Shell/TrayIcon.cs       # 托盘控制
```

---

## 5. 里程碑(估计)

| 阶段 | 内容 | 工作量 |
|---|---|---|
| M1 音频管道 | loopback 捕获 + FFT,控制台/文本验证频谱数值 | 0.5~1 天 |
| M2 可视化 | 环形频谱窗口(固定位置) | 1~2 天 |
| M3 鼠标跟随 | 透明置顶 + 点击穿透 + GetCursorPos 跟随 | 0.5~1 天 |
| M4 打磨 | DPI/多屏、托盘、样式/强度设置、平滑调参 | 1~2 天 |
| M5 发布 | README、截图、单文件发布(可后续加) | 0.5 天 |

---

## 6. 风险与备注

- **WASAPI loopback 权限**:通常默认可用;个别虚拟声卡/蓝牙设备可能异常,需设备回退策略(枚举默认设备)。
- **全屏独占游戏**:透明悬浮窗不显示在独占全屏上,属正常限制。
- **CPU 占用**:渲染目标 <5% CPU,峰值 <10%。
- **无 LICENSE 项目**:仅参考思路,不复制任何代码。
- **Windows 版本差异**:Win11 24H2 后 WASAPI 行为有细微变化,发布前需实测。
