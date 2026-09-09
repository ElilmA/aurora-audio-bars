# 项目记忆 - Mouse Audio Visualizer

> 更新:2026-09-09 (v4)

## 声道分离（2026-09-09，新增）

- **需求**：左条=左声道、右条=右声道，托盘可开关。
- **捕获**：AudioCapture 从「单声道混音 ring」改为 **L/R 双 ring**。帧内按通道索引奇偶拆分：偶索引→左、奇索引→右（立体声=原生 L/R；单声道右镜像左；5.1/7.1 近似左右下混）。API：`ReadLR(left,right,count)` 分声道读；`Read()` 保留为左右均值=原复合行为（SpectrumProbe 兼容）。
- **分析**：AudioEngine 维护 **3 组**滚动窗口与 SpectrumEngine（mono 复合 + L + R，各自独立 FFT/AGC/平滑）。`ChannelSplit`(volatile) 托盘实时切换：分离→每声道独立 Analyze；复合→左右均值窗口单引擎 Analyze。暴露 `Left/Right` 属性（分离=L/R，复合=同一复合帧），不再有 `Current`。
- **渲染**：EdgeOverlay.Render 分别用 `Left`/`Right` 画左右条。默认关闭（行为与旧版完全一致）。
- 声道分离开启后 AGC 各自独立 → 左右音量差会真实反映左右电平差。

## 发布状态(2026-09-09)

- **仓库**:github.com/ElilmA/aurora-audio-bars
- **首个 Release**:v1.0.0(非草稿、正式版),https://github.com/ElilmA/aurora-audio-bars/releases/tag/v1.0.0
- **发布命令**:`dotnet publish src/MouseAudioVisualizer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true` → 单文件 exe ≈72MB(含运行时+压缩)
- **资产**:MouseAudioVisualizer.exe(71851451 B)+ Aurora-Audio-Bars-v1.0.0-win-x64.zip(66265058 B,含 exe+README 改名的"使用说明.txt"),输出到 bin/Release/publish-v1.0.0/ 且被 gitignore
- **上传经验**:一次 `gh release create` 带两个大资产会因 ~300s 超时中断 → 先建 draft 再逐条 `gh release upload --clobber` 可完成;上传完需 `gh release edit --draft=false` 转正式
- tag v1.0.0 已推送 origin

## 项目目标

捕获 Windows 系统音频,将实时频谱渲染为**屏幕左右两侧的彩色竖向频谱条**(1mm 宽、全屏高、透明置顶、点击穿透)。最初为"跟随鼠标的环形频谱",2026-09-08 按用户要求改为屏幕两侧竖条。

## 关键决策

- **技术栈**:C# .NET 8 + WPF + NAudio 2.2.1(MIT),WinForms 仅用于托盘(NotifyIcon)。
- **渲染方案:WPF 自绘(WriteableBitmap)** 已选定并实测通过。2 个竖向条窗口(左/右),每个 ~4x1087 物理像素,unsafe 直接写 BackBuffer,逐段渲染(48 段=1087/48≈22px 高),无查找表(像素少,无需优化)。
- **FFT**:NAudio `FastFourierTransform`(2048 点,Hann 窗,50% 重叠,~47fps)。注意 NAudio FFT 输出已按 N 归一化,单位幅度正弦波峰值约 0.5,**不能再除以 half**。
- **归一化/增益**:AGC(自动增益)跟踪慢速峰值,把整体电平映射到目标 0.75,增益限幅 [0.5, 40]。默认 Gain=1.0。
- **平滑参数**:Attack=0.45 / Decay=0.12(实测合理)。AGC attack=0.05 / decay=0.0015。
- **窗口**:两个 BarWindow(左右),AllowsTransparency + WindowStyle.None + WS_EX_TRANSPARENT|TOOLWINDOW|NOACTIVATE|LAYERED。
- **定位**:虚拟屏幕边界用 GetSystemMetrics(SM_XVIRTUALSCREEN 等),左条贴 vmLeft,右条贴 vmRight-barW。barW = 1mm @96dpi ≈ 4px。
- **窗口/分辨率适配**(2026-09-08):EdgeOverlay 重构为**每个条窗口自身用真实 DPI(GetDpiForWindow)换算物理尺寸**;锚点=虚拟屏幕左右缘+顶;SetWindowPos 显式物理定位定尺寸(不再依赖 WPF 布局 DIP 时机);监听 SystemEvents.DisplaySettingsChanged / UserPreferenceChanged 与窗口 DpiChanged → Relayout 重算,适配分辨率/DPI/全屏变化。左右条分别 isRight 标记。
- **视觉**:(v5 Aurora,2026-09-08)完整 360° 色相连续渐变(紫→蓝→青→绿→黄→橙→红→粉→紫),无硬切;hue drift 12s/圈极缓流动;左右相位不同(左 hueStart=270 紫系、右 180 青系);顶部液态圆头=bloom 隆起+16px 光晕尾;x 向中心亮边缘淡;饱和度/亮度微随音量;每帧整图 BGRA 清零(防残影)。
- **响应速度**(2026-09-08 优化):渲染 16ms/帧(60fps);smoothH attack=0.72 / release=0.30(↑≈30-50ms,↓≈100ms);频带平滑 attack=0.6/decay=0.35;AGC attack=0.12/decay=0.05。实测 0.5s 间歇音:0.68→0.04→0.69→0.04→0.6→0.08 全部跟随,无滞后。链路:WASAPI(46ms 块)→FFT→attack/decay→smoothH→渲染,trail 独立不参与 currentHeight。
- **拖影**:4 帧历史环,alpha 0.16/0.11/0.07/0.04,≤8% 主体高+6px 柔边,主体后画覆盖。
- **DPI**:manifest 声明 PerMonitorV2;条宽按 96 DPI 换算(多 DPI 混用时略有偏差,已记录为已知限制)。
- **入口**:自定义 Program.Main(无 App.xaml),Application.Run 阻塞 + Startup 事件启动 AppHost。

## 重要教训(Bug 记录)

1. **AudioEngine 后台线程必须节流!** 最初循环无 Sleep 节流,数据充足时无限高速 FFT 空转,CPU 吃满单核(98%)。修复:Stopwatch 时间戳节流 ~21ms/帧,CPU 降至 ~0%/1%。
2. 测试环境有**真人移动鼠标**,坐标验证要用"光标静止时立即读窗口"的方式,避免误判。
3. **负坐标副屏 CopyFromScreen 截图会得到纯色/白色**(不正确),验证副屏窗口内容用 PrintWindow API。
4. PowerShell 测试环境可能与应用 DPI 感知不同,坐标对比时先确认 GetDpiForWindow 值。

## 验证结果(2026-09-08,本机 16 核 / 双显示器 / 96 DPI)

- probe 捕获 48kHz/2ch,440Hz 落在正确频带,峰值 0.87(AGC 生效)
- 左右条定位:右条(1916,0,1920,1087)、左条(-1920,0,-1916,1087),均 4px 宽全高
- PrintWindow 验证左条渲染:渐变颜色(紫色系,红绿蓝随 y 变化)35/36 行
- 拉伸效果:右条截图可见粉/紫渐变
- CPU 播放时 ~0-1.17% 单核;点击穿透样式位 0x080800A8 全部生效
- 双屏跟随/定位正确

## 文件结构

- src/MouseAudioVisualizer/:Program.cs(入口+宿主)、Audio/(AudioCapture/SpectrumEngine/SpectrumFrame/AudioEngine)、Visual/(EdgeOverlay/EdgeRenderer/Native)、Shell/TrayIcon.cs、app.manifest
- src/SpectrumProbe/:控制台验证工具(--auto [--play wav] [--secs N])
- **注意:CursorOverlay.cs / RingRenderer.cs 已在 v2 删除**(环形/波纹样式移除,改为边缘竖条)

## 风险/待办

- 真实音乐内容未经全面测试;平滑/AGC 参数可能需按音乐类型微调。
- 多 DPI 显示器混用(如 150% 屏)时条宽换算可能不准,需实测。
- 蓝牙/虚拟声卡 loopback 异常未测。
- 全屏独占游戏不显示悬浮层(系统限制)。
- 无 LICENSE 项目(CursorTrail/chromascope)仅参考,未复制代码。
- **发布产物为自包含 win-x64,尚未验证真机(非本机)Win10/Win11 运行与杀软误报情况。**