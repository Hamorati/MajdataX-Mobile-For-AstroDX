# MajdataX 维护交接文档

> 本文件是项目的"记忆"。**新对话/新维护者接手时，第一件事就是读本文件**，即可完整重建上下文。
> 同步存在于本地 `D:\Workspace\docs\` 与 GitHub 仓库 `Hamorati/MajdataX-Mobile`（docs/ 目录）。

## 1. 项目是什么

- **MajdataViewX**：MajdataX 渲染器（Unity 6000.3.19f1 工程，`D:\Workspace\src\MajdataViewX`）。
  同一工程产出两个端：
  - 桌面渲染器（`BuildScript.BuildWindows64`，与桌面编辑器 MajdataEdit-Neo 配合）
  - Android 移动版 APK（`BuildScript.BuildAndroid`，内置移动端编辑器，见 `Assets/Scripts/Mobile/`）
- **MajdataEdit-Neo**：桌面 WPF 编辑器（`D:\Workspace\src\MajdataEdit-Neo`）。
- 核心目标：谱面解析/渲染**对齐 AstroDX 与 SimaiSharp**（滑条头星与启动拍分离一拍、星星头计入 Tap 物量等）。

## 2. GitHub 归档（权威备份）

| 仓库 | 内容 |
|---|---|
| https://github.com/Hamorati/MajdataX-Mobile | Unity 工程源码 + docs/ + tools/CountCheck + tests/（默认分支 master） |
| https://github.com/Hamorati/MajdataX-Desktop | MajdataEdit-Neo 源码（默认分支 main） |

- 成品：两个仓库的 Releases v1.0.0（`MajdataX-Mobile.apk` / `MajdataX-Desktop-Release.zip`）。
- 本地仓库同时保有 `origin`（上游 re-poem 等）与 `ssh`（GitHub 个人仓库）两个 remote。
- **每次完成一批维护改动后，commit 并 push 回 GitHub**，保持远端为最新真相。

## 3. 工具链（全部在 D:\Tools，不可删除）

| 工具 | 路径 | 用途 |
|---|---|---|
| Unity Editor | `D:\Tools\Unity\6000.3.19f1\Editor\Unity.exe` | 全部构建 |
| Android SDK | `D:\Tools\AndroidSdk`（adb 在 `platform-tools`，模拟器在 `emulator`） | APK 构建/调试 |
| Android NDK r27c | `D:\Tools\AndroidNdk\android-ndk-r27c` | IL2CPP 构建 |
| JDK 17 | `D:\Tools\Jdk\jdk-17.0.2` | Gradle/jar |
| gh CLI | `D:\Tools\gh\bin\gh.exe`（SSH 密钥 `D:\Tools\gh\id_ed25519`） | GitHub 操作 |
| dotnet | 系统安装 `C:\Program Files\dotnet` | E2E/测试 |

**磁盘联接（勿动）**：`.gradle`、`.android`、`.nuget`、`AppData\Local\Unity` 四个用户目录已迁移到 `D:\Tools\ProjectCaches\` 并在原位置建立 Junction，工具按原路径访问。

## 4. 构建命令

### Android APK
```powershell
$env:MAJDATA_ANDROID_SDK="D:\Tools\AndroidSdk"
$env:MAJDATA_ANDROID_NDK="D:\Tools\AndroidNdk\android-ndk-r27c"
$env:MAJDATA_ANDROID_JDK="D:\Tools\Jdk\jdk-17.0.2"
& "D:\Tools\Unity\6000.3.19f1\Editor\Unity.exe" -batchmode -nographics -quit `
  -projectPath D:\Workspace\src\MajdataViewX -executeMethod BuildScript.BuildAndroid `
  -buildOutPath D:\Workspace\build\Android -logFile D:\Workspace\build\unity-android.log
```
日志末尾出现 `BUILD RESULT: Succeeded` 即成功（Unity 包装进程会提前退出，须轮询 log）。

### 桌面渲染器
```powershell
& "D:\Tools\Unity\6000.3.19f1\Editor\Unity.exe" -batchmode -nographics -quit `
  -projectPath D:\Workspace\src\MajdataViewX -executeMethod BuildScript.BuildWindows64 `
  -buildOutPath D:\Workspace\build\Win64 -logFile D:\Workspace\build\unity-win.log
```
部署：把 `build\Win64\*` 覆盖到 `D:\Workspace\MajdataX`（先杀旧进程）。

### 桌面编辑器（WPF）
Visual Studio 2022 打开 `D:\Workspace\src\MajdataEdit-Neo`，Release 构建；产物 `bin\Release\...\MajdataEdit-Neo.exe`。

## 5. 验证流程（改动后必做）

1. **移动端冒烟**（模拟器 AVD `majdata`）：
   ```powershell
   & "D:\Tools\AndroidSdk\emulator\emulator.exe" -avd majdata -no-audio -gpu host -no-boot-anim -no-window -memory 6144 -cores 4
   & "D:\Tools\AndroidSdk\platform-tools\adb.exe" wait-for-device
   # 安装 + 启动 + 看日志：
   adb logcat -d -s Unity | Select-String "\[MB\]"
   ```
   谱面目录（设备）：`/storage/emulated/0/Android/data/com.majdata.viewx.mobile/files/Charts/`（外部存储）。
   屏幕状态可经截图 + 像素采样核验（工具见 `D:\Workspace\tools\`，如 ImageProbe/AsciiView）。
2. **桌面 E2E**（期望输出 `E2E-OK`）：
   ```powershell
   dotnet run --project D:\Workspace\tests\ViewXE2E -c Release --no-build -- D:\Workspace 2.zip 5
   ```
   依赖 `D:\Workspace\2.zip` 与 `D:\Workspace\analysis\2\track.mp3`（**不可删**）。
3. **物量口径核验**：`dotnet run --project D:\Workspace\src\MajdataViewX\tools\CountCheck -- <maidata.txt>`，
   与渲染器 `ObjectCounter.CountNoteSum`（AstroDX 口径）比对。

## 6. 工作区目录（删除原则）

| 目录/文件 | 说明 | 可删？ |
|---|---|---|
| `src\` | 源码仓库（含 22GB Unity Library，克隆后重导要很久） | ❌ 不可删（GitHub 有镜像，删了可重新克隆但代价大） |
| `tests\` | E2E + 解析对齐测试（已入 GitHub） | ❌ 建议保留（本地跑回归） |
| `docs\` | 维护文档 | ❌ 不可删 |
| `analysis\` | E2E 谱面 1-5、SlideTest、崩溃日志、`sym-x86_64` 符号表 | ❌ E2E 依赖 `analysis\2`；符号表用于 addr2line |
| `2.zip`、`1-5.zip`、`*.mp4/png` 等根目录测试素材 | E2E/SlideTest 输入 | ❌ `2.zip` 必需，其余建议保留 |
| `tools\` | 验证小工具（截图像素核验等） | 建议保留（共 5MB） |
| `MajdataX\` | 已部署桌面版（E2E 直接可用） | 可删（可重建），保留则 E2E 立即可跑 |
| `build\` | 构建产物/日志/截图（11.7GB） | ✅ 可删（成品已入 GitHub Releases；重建会再生成） |
| `backup\` | 旧桌面二进制备份 | ✅ 可删（GitHub 成品替代） |
| `astrodx-2.2.0.0023\` | AstroDX 参考副本 | ✅ 可删（与 `src\astrodx` 重复） |
| 根目录 `LXGWWenKaiGB-Light.ttf` | 字体 | ✅ 可删（已内置进仓库 `Assets/Resources/Fonts/`） |

## 7. 常见坑（历次排障结论）

- **GitHub 推送**：本机对 GitHub 的 HTTPS 大上传会连接重置；用 SSH 推送：
  `$env:GIT_SSH_COMMAND="ssh -i D:/Tools/gh/id_ed25519 -o StrictHostKeyChecking=no"; git push ssh <branch>`。
  浅克隆必须 `git fetch --unshallow origin` 后才能推（否则远端 unpack 失败）。
  gh 的 API 调用不稳时加 `$env:HTTPS_PROXY="http://127.0.0.1:7897"`（ClashVerge 混合端口）。
- **模拟器在 Unity 构建期间会掉线**（adb offline）：构建完成后 `adb wait-for-device` 再装。
- **adb 无法向 Unity 输入框注入文本**（input text/keyevent 均无效）：输入类验证需用真机软键盘，
  或用截图/设置文件核验。
- **uGUI 按钮颜色相乘**：`Image.color` 与 Button ColorTint 的 normalColor 会相乘；Image 保持白色、
  状态色全放 ColorBlock（`UiSkin.cs` 已有正确范例）。
- **时间轴/物量口径**：星星头（滑条启动拍）计入 Tap；物量总览与 `ObjectCounter.CountNoteSum` 完全一致。
- 设备谱面目录在**外部存储**而非内部存储；用 `adb root` + push 后需 `chown u0_a209:ext_data_rw`。

## 8. 新对话接手指引

开场直接说（示例）：

> 请先阅读 `D:\Workspace\docs\MajdataX-维护交接.md` 和
> `D:\Workspace\docs\MajdataX-Mobile-构建与使用说明.md`，再帮我维护 MajdataX 项目：<具体需求>

新对话据此即可获知：工具链路径、构建命令、验证流程、谱面位置、GitHub 仓库与全部历史坑位。
完成维护后请 commit 并 push 回 GitHub，保持归档最新。
