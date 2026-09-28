# MajdataX Mobile（Android 版 ViewX）构建与使用说明

> **工作区根 = `D:\Workspace\PMXA\`**（2026-09 由旧的 `D:\Workspace\` 迁移而来）。
> 工程位于 `D:\Workspace\PMXA\src\MajdataViewX`；构建日志/截图在 `D:\Workspace\PMXA\analysis\`；
> `build\` 目录当前不存在，构建时自动重建。详见同目录《MajdataX-维护交接.md》。

## 一、产物

- APK：`build\Android\MajdataX-Mobile.apk`（IL2CPP ARM64，minSdk 24 / Android 7.0+）
- 包名：`com.majdata.viewx.mobile`

## 二、功能范围（竖屏分屏）

- **竖屏运行**：上半屏 = 渲染器（MajdataViewX 游戏画面/判定，等比适配手机屏幕、60 帧 + 右上角实时帧数显示），下半屏 = 内置编辑器。
- **时间轴**（重设计版，对齐桌面 SimaiVisualizer；面板顶部整行、高 200px，内含右侧传感区圆盘）：**仅自定义窗口模式**（设置 →「时间轴窗口」0.25~8 秒滑条，默认 8 秒、持久化；已取消整曲总览/8 秒/4 秒三档预设）：窗口内容从右向左流过**固定指针**，指针与渲染器判定中心水平对齐 x=540；波形为**全亮绿 (0,100,0)** 原始采样折线、约 1 点/0.5px（解码 1000 点/秒）；节拍线对齐原生：**每拍白线**、小节线更亮、**BPM 变化处黄线**；**滑条头星与启动拍分离一拍**（头星画在音符拍，虚线本体从 SlideStartTime = 音符拍 + 等待拍 起画，与渲染器/桌面一致）；播放时间**平滑跟随**（桌面同款 time+=0.2×(target−time)）；波形底纹在**播放/保存刷新后保持显示**（同音频复用解码缓存，不消失）；**按下时间轴立即停止播放**（按钮自动变「播放」，松手保持停止），**相对拖动**定位（拖动距离 ↔ 时间差），按住期间每 0.1 秒实时预览，松手最终确认；定位时**光标移动到对应音符代码的精确字符处**（无整行高亮条），文本自动滚动使目标行可见；时间轴与渲染器严格共用谱面时间（含 `&first` 偏移；diag.flag 开启时屏幕左上角显示黄色同步读数：渲染器 NoteTime / 时间轴显示与目标时间 / 音频位置，用于定位剩余偏差）。
- **内置编辑器**：
  - 仅显示**当前难度**的 inote 块（title/artist/des/lv 等元数据在文件菜单维护），难度经「文件」菜单的 8 档按钮切换，保存时无损拼回完整文件；
  - 左侧竖排工具栏（文件/编辑/保存/播放/停止 + 难度标签）+ 下方**物量总览**（Tap/Hold(含TouchHold)/Slide/Touch/Break/合计，仅当前难度，地雷不计；**滑条星星头（启动拍）计入 Tap**，与渲染器/AstroDX 参考口径一致）；
  - 文本浏览：**右侧滚动条（35px 加宽版）+ 可拖滑块是唯一浏览方式**（文本在框内裁剪、右缘留白避开滚动条）；
  - 文本手势：**单击** = 放下光标（不弹键盘）；**单指拖动** = 字符级光标跟随手指（接近边缘自动滚动文本），**松手立即自动呼出输入法**；**长按 ≈0.5 秒后拖动** = 字符级文本选择；**纯长按** = 选中整行；**双击** = 输入法 + 光标定位到点击字符（系统键盘弹出，`shouldHideMobileInput` 直输、无 Unity 预览框）；光标/选区为自绘覆盖层，IME 会话由原生光标接管（呼出后光标行居中显示）；
  - **「编辑」菜单**（文件按钮下方，移植原生 MajdataEdit-Neo）：对选中文本应用 **左右翻转 / 上下翻转 / 180° 翻转 / 45° 顺时针旋转 / 45° 逆时针旋转 / 1.5 倍细分 / 2 倍细分**（含 D/E 触区、q/p、z/s、</>、`<HS*…>` 等特殊映射；变换后保持选中，可连续应用；未选择文本时提示）；
  - 渲染器区域内游戏自带文字（uGUI/TMP/3D TextMesh）全部隐藏，仅保留图形判定/音符/特效；
  - 「文件」菜单：打开谱面（文件夹浏览 + **导入谱面文件夹** + 每行右侧**删除**按钮——确认后删除整个谱面文件夹（maidata/音频/图片全部），删除当前打开谱面时编辑器回到空状态）、新建谱面（**必须先选音频**）、难度（8 档）、等级、作者、偏移、播放速度（**等级/作者/偏移输入即写入谱面文本**，点「保存」落盘；无「应用元数据」按钮）、**导出 zip**（系统目录选择器任选目标位置；把谱面目录中存在的文件——maidata.txt + 音频 + bg 图片 + pv 视频——平铺打包为 `<谱面名>.zip`；导出的是**已保存版本**，若有未保存修改会先提示保存）、**设置…**；
  - 保存后自动重新解析并刷新渲染器；「播放」直接用当前编辑内容（未保存也可试玩），并可从时间轴定位处开始。
- **设置（文件 → 设置…，仅对本应用生效、不写入谱面）**：
  - Tap/Touch 流速滑条、全局声音偏移（-300~+300 ms）；
  - **时间轴窗口**：仅自定义模式，0.25~8 秒滑条（默认 8 秒）；
  - **编辑器背景**：系统选择器选图（等比铺满、可调暗化 0~85%、可清除），持久化于 `CustomBG/`（渲染器背景选项已移除，渲染器统一使用内置背景）；
  - **判定音音量**：与原生 MajdataEdit-Neo 完全一致——**9 类**（Track 音乐 / Answer 应答 / Tap / Slide / Break / BreakSlide / Ex / Touch / Hanabi 花火），滑条 0~100%、默认 90%，实时生效并持久化于 `settings.json`（自定义判定音文件替换功能已移除）。
- **视觉风格**：深色极简扁平（深灰底 + 低饱和青强调色 + 圆角卡片/按钮/输入框），按钮**按下时发光**（按下快亮、松手极速淡出约 0.05 秒、无余辉）；**点击按钮不保持选中高亮**（无点击后发白残留）；仅视觉层换肤，布局坐标与交互不变。编辑器 UI 字体为内置 **霞鹜文楷 GB Light**（`Assets/Resources/Fonts/LXGWWenKaiGB-Light.ttf`，缺失时回退系统字体）。
- **传感区 slide 圆盘**：编辑器右侧的触区圆盘（移植自桌面 TouchSpacePanel），随播放实时显示传感区 slide 的轨迹、节点、头部标记与星标位置。
- **触摸判定仅限上半屏**（下半屏触摸不会误触判定）。
- 支持全部谱面语法与渲染（与桌面 ViewX 同一套代码），含传感区 slide、Touch 特殊皮肤等。
- 独立运行：不依赖桌面编辑器（WS/共享内存通道在 Android 构建中不启动）。
- 音频格式：mp3 / ogg(Vorbis) / wav / flac / aiff（移动端无 ffmpeg，暂不支持 opus/m4a 转码）。
- 暂不支持：录制导出视频（视频导出按需求排除）、桌面编辑器连接。

## 三、安装谱面（两种方式）

**方式 A：App 内导入（推荐，无需电脑）**

1. 把谱面文件夹（内含 `maidata.txt` + `track.*`）放到手机任意可访问位置（如 `Download/MajdataCharts/<谱面名>/`）。
2. 打开 App → 「文件 → 打开谱面」→ 点「导入谱面文件夹…」。
3. 在系统文件夹选择器中选择谱面所在的**父文件夹**（如 `MajdataCharts`），App 会把其中每个含 `maidata.txt` 的子文件夹复制进谱面目录。
4. 回到列表点选谱面 → 编辑/保存/播放。

（Android 11+ 分区存储下应用私有目录无法被文件管理器访问，导入是取谱的唯一通用途径。）

**方式 B：USB/MTP 直接拷贝（调试用）**

- 谱面目录：`Android/data/com.majdata.viewx.mobile/files/Charts/<谱面名>/`
  目录内包含：`maidata.txt`、`track.mp3|ogg|wav|flac|aiff`、可选 `bg.jpg|png`、`pv.mp4|bg.mp4`。
- 该目录在 Android 11+ 上通常需要 adb 或系统文件管理器授权才能写入，普通用户请用方式 A。

## 四、从源码构建

### 4.1 工具链（本机已就绪）

- Unity 6000.3.19f1 + Android Build Support 模块（`D:\Tools\Unity\6000.3.19f1`，Editor 位于 `Editor\Data\PlaybackEngines\AndroidPlayer`，含 Gradle 9.1）
- Android SDK：`D:\Tools\AndroidSdk`（platform-tools、platforms;android-34/35/36、build-tools;34.0.0/35.0.0/36.0.0、cmdline-tools 16.0、emulator + system-images）
- Android NDK **r27c**（Unity 6000.3 要求，EditorPrefs 键 `AndroidNdkRootR27C`）：`D:\Tools\AndroidNdk\android-ndk-r27c`
- OpenJDK 17（EditorPrefs 键 `Jdk17Path`）：`D:\Tools\Jdk\jdk-17.0.2`
- 皮肤资源打包：`Assets\StreamingAssets\assets.zip`（内含 Skin + SFX，首启解压到 persistentDataPath）
- E2E 测试：dotnet SDK（系统安装）`C:\Program Files\dotnet`

### 4.1.1 磁盘布局（缓存已迁移至 D 盘，目录联接保持原路径可用）

构建缓存/模拟器数据原位于 C 盘用户目录，已整体迁移到 `D:\Tools\ProjectCaches\`，并在原位置建立**目录联接（Junction）**，所有工具仍按原路径访问、无需改配置：

| C 盘原路径（联接） | 实际存储位置 | 内容 |
|---|---|---|
| `C:\Users\<用户>\.gradle` | `D:\Tools\ProjectCaches\gradle` | Android Gradle 缓存（约 20 GB） |
| `C:\Users\<用户>\.android` | `D:\Tools\ProjectCaches\android-home` | AVD（majdata 模拟器）+ adb 密钥（约 13 GB） |
| `C:\Users\<用户>\.nuget` | `D:\Tools\ProjectCaches\nuget` | NuGet 包缓存（E2E 测试，约 2 GB） |
| `C:\Users\<用户>\AppData\Local\Unity` | `D:\Tools\ProjectCaches\unity-local` | Unity 编辑器缓存/偏好 |

如需还原：删除联接（`rmdir <联接路径>`）后把 `D:\Tools\ProjectCaches\<目录>` 移回即可。

### 4.2 构建命令

```powershell
$env:MAJDATA_ANDROID_SDK = 'D:\Tools\AndroidSdk'
$env:MAJDATA_ANDROID_NDK = 'D:\Tools\AndroidNdk\android-ndk-r27c'
$env:MAJDATA_ANDROID_JDK = 'D:\Tools\Jdk\jdk-17.0.2'
& 'D:\Tools\Unity\6000.3.19f1\Editor\Unity.exe' -batchmode -nographics -quit `
  -projectPath 'D:\Workspace\PMXA\src\MajdataViewX' `
  -executeMethod BuildScript.BuildAndroid `
  -buildOutPath 'D:\Workspace\PMXA\build\Android' `
  -logFile 'D:\Workspace\PMXA\analysis\unity-android-build.log'
```

`BuildScript.BuildAndroid` 会自动：
- 创建 `Assets/Scenes/Bootstrap.unity`（首启资源解压场景，场景列表 = Bootstrap + Game）；
- 设置 IL2CPP/ARM64、包名、自动旋转、minSdk 24、默认图形 API；
- 从 `MAJDATA_ANDROID_SDK/NDK/JDK` 环境变量注入外部工具路径（EditorPrefs）。

### 4.3 关键改动文件

| 文件 | 说明 |
|---|---|
| `Assets\Scripts\Mobile\MobileBootstrap.cs` | 首启解压 + 运行时构建选谱 UI + 本地加载/播放流程 |
| `Assets\Scripts\Mobile\MobileTrackFile.cs` | 移动端 track 音频查找 |
| `Assets\Scripts\Managers\PlayManager.cs` | 新增 `LoadChartAsync` / `LoadLocalChartAsync`（本地解析，不依赖 MMF） |
| `Assets\Scripts\WsServer.cs` | Android 构建下不启动 WS 服务 |
| `Assets\Scripts\Base\MajEnv.cs` | Android 下运行资源目录 = persistentDataPath |
| `Assets\Editor\BuildScript.cs` | `BuildAndroid` 入口 + 外部工具注入 + Bootstrap 场景生成 |
| `Assets\Plugins\Android\libs\arm64-v8a\libbass.so` | BASS Android 原生库 |
| `Assets\StreamingAssets\assets.zip` | Skin + SFX 打包（首启解压） |

## 五、iOS（IPA）说明

IPA 无法在 Windows 上构建：需要 macOS + Xcode，且分发需 Apple 开发者账号签名。
若要出 iOS 版：在 Mac 上安装 Unity 6000.3.19f1 + iOS Build Support，打开同一份工程，
勾选 iOS 平台构建即可（本项目代码均为托管代码，iOS 仅需把 `libbass.so` 换成
un4seen 提供的 `libbass.a` 静态库并放在 `Assets\Plugins\iOS`）。
