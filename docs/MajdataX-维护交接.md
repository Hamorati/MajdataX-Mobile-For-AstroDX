# MajdataX 维护交接文档

> 本文件是项目的"记忆"。**新对话/新维护者接手时，第一件事就是读本文件**，即可完整重建上下文。
> 同步存在于本地 `D:\Workspace\PMXA\docs\` 与 GitHub 仓库 `Hamorati/MajdataX-Mobile`（docs/ 目录）。

> ## 工作区根 = `D:\Workspace\PMXA\`
> 2026-09 由旧的 `D:\Workspace\` 整体迁移而来，本文档中的路径已全部同步。
> - 现有目录：`src\`（源码仓库 + 现成构建产物）、`tests\`、`tools\`、`docs\`、`analysis\`（全部日志/截图/测试谱面 1-5）。
> - **不存在**的目录：`build\`（构建产物根，下次构建自动重建）、`MajdataX\`（桌面部署目录，恢复方式见 §5）。
> - 桌面版现成产物在 `src\MajdataViewX\Build\Win64\`（2026-09-10 构建，含 `MajdataViewX.exe` / `GameAssembly.dll` / `UnityPlayer.dll`），复制出来即可免重建使用。

## 1. 项目是什么

- **MajdataViewX**：MajdataX 渲染器（Unity 6000.3.19f1 工程，`D:\Workspace\PMXA\src\MajdataViewX`）。
  同一工程产出两个端：
  - 桌面渲染器（`BuildScript.BuildWindows64`，与桌面编辑器 MajdataEdit-Neo 配合）
  - Android 移动版 APK（`BuildScript.BuildAndroid`，内置移动端编辑器，见 `Assets/Scripts/Mobile/`）
- **MajdataEdit-Neo**：桌面 WPF 编辑器（`D:\Workspace\PMXA\src\MajdataEdit-Neo`）。
- 核心目标：谱面解析/渲染**对齐 AstroDX 与 SimaiSharp**（滑条头星与启动拍分离一拍、星星头计入 Tap 物量等）。

## 2. GitHub 归档（权威备份）

> ⚠️ **两个仓库均已改名**（2026-09-29 实测核对）。旧 URL 仍可访问并由 GitHub **自动重定向**，所以 `git push ssh`、`gh release list` 等旧写法依旧可用、无需改配置：
> - `Hamorati/MajdataX-Mobile` → **`Hamorati/MajdataX-Mobile-For-AstroDX`**
> - `Hamorati/MajdataX-Desktop` → **`Hamorati/MajdataX-For-AstroDX`**

| 仓库（新名；旧名可重定向） | 内容 |
|---|---|
| https://github.com/Hamorati/MajdataX-Mobile-For-AstroDX （旧 `MajdataX-Mobile`） | Unity 工程源码 + docs/ + tools/CountCheck + tests/（默认分支 master） |
| https://github.com/Hamorati/MajdataX-For-AstroDX （旧 `MajdataX-Desktop`） | MajdataEdit-Neo 源码（默认分支 main） |

- 成品：两个仓库的 Releases v1.0.0（`MajdataX-Mobile v1.0.0（AstroDX 对齐最终版）` / `MajdataX-Desktop v1.0.0（渲染器成品包）`，均发布于 2026-09-10）。
- 本地仓库同时保有 `origin`（上游 re-poem 等）、`ssh` 与 `github`（GitHub 个人仓库；remote URL 仍写旧名，靠重定向工作）三个 remote。
- **每次完成一批维护改动后，commit 并 push 回 GitHub**，保持远端为最新真相。
- 推送命令：`$env:GIT_SSH_COMMAND="ssh -i D:/Tools/gh/id_ed25519 -o StrictHostKeyChecking=no"; git push ssh <branch>`（其他坑见 §7）。

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

> `build\` 根目录已被清理，构建时会自动重建。**日志统一写 `analysis\`**（历史日志也都在那里）。

### Android APK
```powershell
$env:MAJDATA_ANDROID_SDK="D:\Tools\AndroidSdk"
$env:MAJDATA_ANDROID_NDK="D:\Tools\AndroidNdk\android-ndk-r27c"
$env:MAJDATA_ANDROID_JDK="D:\Tools\Jdk\jdk-17.0.2"
& "D:\Tools\Unity\6000.3.19f1\Editor\Unity.exe" -batchmode -nographics -quit `
  -projectPath D:\Workspace\PMXA\src\MajdataViewX -executeMethod BuildScript.BuildAndroid `
  -buildOutPath D:\Workspace\PMXA\build\Android -logFile D:\Workspace\PMXA\analysis\unity-android-build.log
```
日志末尾出现 `BUILD RESULT: Succeeded` 即成功（Unity 包装进程会提前退出，须轮询 log）。
产物：`D:\Workspace\PMXA\build\Android\MajdataX-Mobile.apk`。
（Gradle 中间产物另存于 `src\MajdataViewX\Library\Bee\Android\Prj\IL2CPP\Gradle\launcher\build\outputs\apk\release\launcher-release.apk`。）

### 桌面渲染器
```powershell
& "D:\Tools\Unity\6000.3.19f1\Editor\Unity.exe" -batchmode -nographics -quit `
  -projectPath D:\Workspace\PMXA\src\MajdataViewX -executeMethod BuildScript.BuildWindows64 `
  -buildOutPath D:\Workspace\PMXA\build\Win64 -logFile D:\Workspace\PMXA\analysis\unity-win.log
```
部署：把 `build\Win64\*` 覆盖到 `D:\Workspace\PMXA\MajdataX`（先杀旧进程）。

### 桌面编辑器（WPF）
Visual Studio 2022 打开 `D:\Workspace\PMXA\src\MajdataEdit-Neo`，Release 构建；产物 `bin\Release\...\MajdataEdit-Neo.exe`。

## 5. 验证流程（改动后必做）

1. **移动端冒烟**（模拟器 AVD `majdata`）：
   ```powershell
   & "D:\Tools\AndroidSdk\emulator\emulator.exe" -avd majdata -no-audio -gpu host -no-boot-anim -no-window -memory 6144 -cores 4
   & "D:\Tools\AndroidSdk\platform-tools\adb.exe" wait-for-device
   # 安装 + 启动 + 看日志：
   adb logcat -d -s Unity | Select-String "\[MB\]"
   ```
   谱面目录（设备）：`/storage/emulated/0/Android/data/com.majdata.viewx.mobile/files/Charts/`（外部存储）。
   屏幕状态可经截图 + 像素采样核验（工具见 `D:\Workspace\PMXA\tools\`，如 ImageProbe/AsciiView）。
2. **桌面 E2E**（期望输出 `E2E-OK`）：
   ```powershell
   dotnet run --project D:\Workspace\PMXA\tests\ViewXE2E -c Release --no-build -- D:\Workspace\PMXA 2.zip 5
   ```
   - 谱面依赖：`D:\Workspace\PMXA\analysis\2\`（`maidata.txt` + `track.mp3`，**不可删**）。`analysis\1`~`5` 为历次 E2E/解析核验谱面，建议全部保留。
   - **命令里的 `2.zip` 只是标签参数**：`Program.cs` 用 `chartName.Replace(".zip","")` 定位 `analysis\2` 与截图名 `e2e-2.png`，**并不需要真实的 `2.zip` 文件**。旧根目录的 `2.zip`/`1-5.zip` 在 2026-09 迁移时已不存在，也无需恢复。
   - 另需已部署桌面版 `D:\Workspace\PMXA\MajdataX`（`Program.cs:32` 要求 `<root>\MajdataX\MajdataViewX.exe`；该目录 2026-09-10 收尾时已删除）。恢复方式（任选其一）：
     - **最快**：把现成产物 `D:\Workspace\PMXA\src\MajdataViewX\Build\Win64\*` 复制成 `D:\Workspace\PMXA\MajdataX\`，无需重新构建；
     - `gh release download v1.0.0 --repo Hamorati/MajdataX-Desktop --dir D:\Workspace\PMXA\build\rel`，解压 zip 内容到 `D:\Workspace\PMXA\MajdataX`；
     - 或重新 `BuildWindows64` 后把 `build\Win64\*` 部署到该目录。
3. **物量口径核验**：`dotnet run --project D:\Workspace\PMXA\src\MajdataViewX\tools\CountCheck -- <maidata.txt>`，
   与渲染器 `ObjectCounter.CountNoteSum`（AstroDX 口径）比对。

## 6. 工作区目录（删除原则）

| 目录/文件 | 说明 | 可删？ |
|---|---|---|
| `src\` | 源码仓库（含 22GB Unity Library，克隆后重导要很久）；**内含现成桌面产物 `src\MajdataViewX\Build\Win64\`，可直接部署免重建** | ❌ 不可删（GitHub 有镜像，删了可重新克隆但代价大） |
| `tests\` | E2E + 解析对齐测试（已入 GitHub） | ❌ 建议保留（本地跑回归） |
| `docs\` | 维护文档（本文件 + Mobile 构建与使用说明 + 进度存档） | ❌ 不可删 |
| `analysis\` | E2E 谱面 1-5、SlideTest、**全部构建/崩溃日志与截图**、`sym-x86_64` 符号表 | ❌ E2E 依赖 `analysis\2`；符号表用于 addr2line |
| `tools\` | 验证小工具（截图像素核验等） | 建议保留（共 5MB） |
| 旧根目录 `2.zip`/`1-5.zip`/`*.mp4/png` | 旧测试素材 | ✅ 已不存在（2026-09 迁移时清理）；谱面已解包在 `analysis\1`~`5`，**E2E 不再需要 zip** |
| `MajdataX\` | 已部署桌面版（E2E 直接可用） | ✅ 已于 2026-09-10 删除；**恢复：复制 `src\MajdataViewX\Build\Win64\*` 到 `D:\Workspace\PMXA\MajdataX\`**（或 GitHub Release 解压，或重建部署） |
| `build\` | 构建产物/日志/截图 | ✅ 已于 2026-09-10 删除（成品已入 GitHub Releases；下次构建自动重建；历史日志/截图现存于 `analysis\`） |
| `backup\` | 旧桌面二进制备份 | ✅ 已删除 |
| `astrodx-2.2.0.0023\` | AstroDX 参考副本 | ✅ 已删除（与 `src\astrodx` 重复） |
| 根目录 `LXGWWenKaiGB-Light.ttf` | 字体 | ✅ 已删除（已内置进仓库 `Assets/Resources/Fonts/`） |

## 7. 常见坑（历次排障结论）

- **GitHub 推送**：本机对 GitHub 的 HTTPS 大上传会连接重置；用 SSH 推送：
  `$env:GIT_SSH_COMMAND="ssh -i D:/Tools/gh/id_ed25519 -o StrictHostKeyChecking=no"; git push ssh <branch>`。
  浅克隆必须 `git fetch --unshallow origin` 后才能推（否则远端 unpack 失败）。
  gh 的 API 调用不稳时加 `$env:HTTPS_PROXY="http://127.0.0.1:7897"`（ClashVerge 混合端口）。
- **文档有两份副本**：`D:\Workspace\PMXA\docs\`（工作副本）与 `src\MajdataViewX\docs\`（受 git 跟踪、会 push 到 GitHub）。
  改文档后**两份都要更新**，否则远端仍是旧内容（`Mobile-进度存档-重启后继续.md` 只在工作副本里）。
- **模拟器在 Unity 构建期间会掉线**（adb offline）：构建完成后 `adb wait-for-device` 再装。
- **adb 无法向 Unity 输入框注入文本**（input text/keyevent 均无效）：输入类验证需用真机软键盘，
  或用截图/设置文件核验。
- **uGUI 按钮颜色相乘**：`Image.color` 与 Button ColorTint 的 normalColor 会相乘；Image 保持白色、
  状态色全放 ColorBlock（`UiSkin.cs` 已有正确范例）。
- **时间轴/物量口径**：星星头（滑条启动拍）计入 Tap；物量总览与 `ObjectCounter.CountNoteSum` 完全一致。
- 设备谱面目录在**外部存储**而非内部存储；用 `adb root` + push 后需 `chown u0_a209:ext_data_rw`。
- **Unity 批处理构建**：包装进程提前退出 ≠ 构建结束，须轮询 log 里的 `BUILD RESULT`；Burst/bcl 阶段约 13 分钟
  CPU 几乎不动属正常，勿误杀。

## 8. 新对话接手指引

开场直接说（示例）：

> 请先阅读 `D:\Workspace\PMXA\docs\MajdataX-维护交接.md` 和
> `D:\Workspace\PMXA\docs\MajdataX-Mobile-构建与使用说明.md`，再帮我维护 MajdataX 项目：<具体需求>

新对话据此即可获知：工作区根、工具链路径、构建命令、验证流程、谱面位置、GitHub 仓库与全部历史坑位。
完成维护后请 commit 并 push 回 GitHub，保持归档最新。
