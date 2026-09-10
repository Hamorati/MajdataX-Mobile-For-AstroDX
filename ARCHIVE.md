# MajdataX-Mobile（MajdataViewX + Android 移动版）归档说明

本仓库为 MajdataX 渲染器 **MajdataViewX** 的 Unity 工程完整源码（基于 re-poem/MajdataViewX），
同时包含本项目全部改动：

- **AstroDX 兼容**：Simai 解析/渲染对齐 AstroDX 与 SimaiSharp 口径（滑条头星与启动拍分离一拍、
  星星头计入 Tap 物量、节拍线/BPM 变化线等）
- **Android 移动端**（`Assets/Scripts/Mobile/`）：内置编辑器（分屏、时间轴、按难度分块编辑、
  谱面导入/删除/导出 zip、判定音 9 类音量、深色扁平 UI + 按压发光、霞鹜文楷字体）、
  SAF 系统选择器原生桥（`NativePlugins/MajdataPicker`，jar 构建脚本见该目录）、IL2CPP 双 ABI 构建
- 桌面渲染器亦由本工程构建（`BuildScript.BuildWindows64`），桌面编辑器源码见姊妹仓库 MajdataX-Desktop

## 构建（详见 docs/）
- 工具链：Unity 6000.3.19f1 + Android Build Support（`D:\Tools\Unity`）、
  Android SDK/NDK r27c/JDK17（`D:\Tools\AndroidSdk` / `D:\Tools\AndroidNdk` / `D:\Tools\Jdk`）
- Android APK：`BuildScript.BuildAndroid`（Bootstrap + Game 场景，IL2CPP ARM64+x86_64，minSdk 24）
- 桌面渲染器：`BuildScript.BuildWindows64`

## 关键目录
- `Assets/Scripts/Mobile/` 移动端编辑器全部逻辑
- `Assets/Editor/BuildScript.cs` 命令行构建入口
- `Assets/Plugins/Android/` 自定义 Manifest、MajdataPicker.jar、libbass
- `NativePlugins/MajdataPicker/` SAF 选择器 Java 源码与 jar 重建流程
- `Assets/Resources/Fonts/` 内置字体（霞鹜文楷 GB Light）
- `docs/MajdataX-Mobile-构建与使用说明.md` 完整构建与使用文档
- `tools/CountCheck/` 物量口径核验工具（用与 APK 相同的 MajSimai 2.2.3 统计并比对 AstroDX 口径）

## 成品
- Android APK 见本仓库 Releases（MajdataX-Mobile.apk，含全部修复的最终 build）
- 桌面渲染器成品见姊妹仓库 MajdataX-Desktop 的 Releases
