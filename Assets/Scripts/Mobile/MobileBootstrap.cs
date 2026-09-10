#nullable enable

using Cysharp.Threading.Tasks;
using MajdataViewX.Managers;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.MajSetting;
using MajSimai;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// Android 独立运行入口（竖屏分屏）：
    /// 上半屏 = 渲染器（ViewX 游戏画面，触摸判定仅限上半屏）；
    /// 下半屏 = 内置编辑器（maidata.txt 文本编辑 + 保存 + 播放/停止 + "文件"菜单：
    /// 打开/新建谱面、难度、等级、作者、偏移、播放速度）。
    /// 首次启动由 Bootstrap 场景解压 Skin/SFX 后进入 Game 场景。
    /// </summary>
    public class MobileBootstrap : MonoBehaviour
    {
        public static MobileBootstrap? Instance { get; private set; }

        private static readonly string[] DiffNames =
            { "EASY", "BASIC", "ADVANCED", "EXPERT", "MASTER", "Re:MASTER", "ORIGINAL", "UTAGE" };

        private Font? _uiFont;

        private GameObject? _editorRoot;
        private GameObject? _dialogRoot;
        private GameObject? _dialogPanel;
        private MobileInputField? _textInput;
        private Text? _statusText;        private Text? _diffLabel;
        private Text? _fpsText;
        private Text? _syncReadout; // 时间同步读数（diag.flag 时显示）
        private Button? _playStopButton;
        private MobileTimeline? _timeline;
        private GameObject? _caretOverlay;  // 自绘光标覆盖层（未聚焦时）
        private GameObject[] _selOverlays = Array.Empty<GameObject>(); // 自绘选区条带池
        private const int MaxSelectionBands = 24;
        private Text[] _countTexts = Array.Empty<Text>(); // 物量总览
        private RectTransform? _bgRect;   // 编辑器自定义背景
        private Image? _bgImage;
        private Image? _bgDimImage;
        private Text? _bgDimValueLabel;
        private bool _pendingBgPick;      // 正在选择背景图片（编辑器）
        private SimaiFile? _parsedFile;   // 已解析的完整谱面（偏移等元数据）
        private double _chartOffset;      // first 偏移（时间轴与渲染器同步用）
        private MobileTextScrollbar? _textScrollbar; // 文本滚动条
        private readonly MajVolumeSetting _volumeSetting = new();

        // ---- 批次B：时间轴 + 按难度分块编辑 + 定位高亮 ----
        private double _chartDuration;
        private double _pendingStart;
        private SimaiChart _parsedChart = SimaiChart.Empty;
        private int[] _blockLineFullIndices = Array.Empty<int>(); // 块视图中每行对应的完整文件行号
        private int[] _blockLineCharStarts = Array.Empty<int>();  // 块视图中每行的字符起点

        private static readonly object LogLock = new();
        private static string CrashLogPath => Path.Combine(Application.persistentDataPath, "crash-mobile.log");

        private string _currentFolder = string.Empty;
        private string _fileText = string.Empty;
        private int _difficulty;
        private float _speed = 1f;
        private bool _playing;
        private bool _gameReady;
        private static bool _gameReadyDone; // 跨实例保护：编辑器 UI 只构建一次
        private bool _busy;
        private string? _stagedAudioPath;
        private Text? _pickedAudioLabel;
        // 波形缓存：播放/保存刷新会重建时间轴（SetChart 清空波形），同音频无需重复解码，直接复用
        private float[]? _lastWaveform;
        private double _lastWaveDuration;
        private double _lastWaveOffset;
        private string _lastWaveTrack = string.Empty;
        private string? _pendingExportZip;
        private string _pendingExportName = string.Empty;

        public static string ChartsRoot =>
            Path.Combine(Application.persistentDataPath, "Charts");

        private void Awake()
        {
#if UNITY_ANDROID || MAJDATA_MOBILE_DEBUG
            // 单例保护：若因任何原因出现第二个实例，立即销毁，避免重复构建编辑器 UI
            if (Instance is not null && Instance != this)
            {
                Debug.LogWarning($"[MB] duplicate instance destroyed, iid={GetInstanceID()}");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
#if UNITY_ANDROID
            // 目标 60 帧：Unity Android 默认上限 30，需显式设置并关垂直同步
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
#endif
            // 崩溃日志：所有 Log/异常写入 persistentDataPath/crash-mobile.log，便于闪退定位
            Application.logMessageReceived += OnUnityLog;
            Debug.Log($"[MB] Awake iid={GetInstanceID()} scene={SceneManager.GetActiveScene().name}");
#endif
        }

        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            try
            {
                lock (LogLock)
                {
                    File.AppendAllText(CrashLogPath,
                        $"[{DateTime.Now:HH:mm:ss}] {type}: {condition}\n{stackTrace}\n\n");
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>移动端构建下自动创建移动端入口（不改动 Game 场景文件）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
#if UNITY_ANDROID || MAJDATA_MOBILE_DEBUG
            if (Instance is not null) return;
            var go = new GameObject("MobileBootstrap");
            go.AddComponent<MobileBootstrap>();
#endif
        }

        private void Start()
        {
#if UNITY_ANDROID || MAJDATA_MOBILE_DEBUG
            DontDestroyOnLoad(gameObject);
            _uiFont = CreateUiFont();
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (SceneManager.GetActiveScene().name == "Bootstrap")
                StartCoroutine(BootstrapAndEnterGame());
            else
                GameReady();
#endif
        }

        private int _diagFrame;
        private Vector2 _lastTouchPos = new Vector2(-9999, -9999);
        private float _fpsAccum;
        private float _fpsTime;
        private int _fpsFrames;
        private bool _textFocusedPrev;
        private float _syncDiagAccum;
        private float _syncDiagAccum2;
        private int _memDiagFrame;
        private void Update()
        {
#if UNITY_ANDROID || MAJDATA_MOBILE_DEBUG
            // 实时帧数（0.25s 平滑刷新）
            _fpsAccum += Time.unscaledDeltaTime;
            _fpsTime += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsTime >= 0.25f)
            {
                if (_fpsText is not null && _fpsAccum > 0.0001f)
                {
                    var fps = _fpsFrames / _fpsAccum;
                    _fpsText.text = $"{fps:0} FPS";
                    _fpsText.color = fps >= 55f
                        ? new Color(0.75f, 1f, 0.75f, 0.95f)
                        : fps >= 35f
                            ? new Color(1f, 1f, 0.6f, 0.95f)
                            : new Color(1f, 0.6f, 0.55f, 0.95f);
                }
                _fpsAccum = 0f;
                _fpsTime = 0f;
                _fpsFrames = 0;
            }
            // 时间轴播放头（播放中取谱面时间=与渲染器同步；空闲显示定位位置；圆盘在时间轴内部随其更新）
            if (_timeline is not null)
                _timeline.SetPlayhead(_playing ? PlayManager.CurrentNoteTime : _pendingStart);
            // 时间轴-渲染器同步诊断（diag.flag 存在且播放中时每 0.25s 记录）
            if (_playing && File.Exists(Path.Combine(Application.persistentDataPath, "diag.flag")))
            {
                _syncDiagAccum += Time.unscaledDeltaTime;
                if (_syncDiagAccum >= 0.25f)
                {
                    _syncDiagAccum = 0f;
                    Debug.Log($"[MB] sync: note={PlayManager.CurrentNoteTime:F3} audioPos={_audioManager?.TrackPositionSeconds:F3} offset={_chartOffset:F3}");
                }
            }
            // 同屏时间读数（diag.flag 存在时每 0.25s 刷新）
            if (_syncReadout is not null)
            {
                var diagOn = File.Exists(Path.Combine(Application.persistentDataPath, "diag.flag"));
                if (diagOn)
                {
                    _syncDiagAccum2 += Time.unscaledDeltaTime;
                    if (_syncDiagAccum2 >= 0.25f)
                    {
                        _syncDiagAccum2 = 0f;
                        _syncReadout.text =
                            $"NT={PlayManager.CurrentNoteTime:F2}  TL显示={_timeline?.DisplayTime:F2}  TL目标={_timeline?.TargetTime:F2}  音频={_audioManager?.TrackPositionSeconds:F2}  档={_timeline?.Zoom}";
                        _syncReadout.gameObject.SetActive(true);
                    }
                }
                else
                {
                    _syncReadout.gameObject.SetActive(false);
                }
            }
            // 输入框焦点切换时刷新自绘光标/选区覆盖层（IME 会话中隐藏）
            if (_textInput is not null)
            {
                var focused = EventSystem.current is not null &&
                              EventSystem.current.currentSelectedGameObject == _textInput.gameObject;
                if (focused != _textFocusedPrev)
                {
                    _textFocusedPrev = focused;
                    RefreshTextOverlays();
                }
            }
            // 周期隐藏游戏文字（游戏会在不同阶段动态生成文字：标题/谱面信息/判定/连击等）
            if (Time.frameCount % 30 == 0)
                HideGameTexts();
            // 内存诊断（diag.flag 存在时每 30 秒记录，用于闲置闪退排查）
            if (++_memDiagFrame >= 1800 &&
                File.Exists(Path.Combine(Application.persistentDataPath, "diag.flag")))
            {
                _memDiagFrame = 0;
                Debug.Log($"[MB] mem: unity={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:F1}MB gc={GC.GetTotalMemory(false) / 1048576.0:F1}MB");
            }
            // 触点诊断（仅 diag.flag 存在时）
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                if (t.position != _lastTouchPos && File.Exists(Path.Combine(Application.persistentDataPath, "diag.flag")))
                {
                    _lastTouchPos = t.position;
                    Debug.Log($"[MB] touch pos={t.position} phase={t.phase}");
                }
            }
            // 音频选择结果轮询
            if (MobileAudioPicker.Picking)
            {
                var (done, path, error) = MobileAudioPicker.TryGetResult();
                if (done)
                {
                    if (_pendingBgPick)
                    {
                        // 编辑器背景图片选择
                        _pendingBgPick = false;
                        if (!string.IsNullOrEmpty(path))
                            InstallPickedBackground(path);
                        else
                            SetStatus("背景选择失败: " + error);
                        ShowSettingsDialog();
                    }
                    else if (!string.IsNullOrEmpty(path))
                    {
                        _stagedAudioPath = path;
                        if (_pickedAudioLabel is not null)
                            _pickedAudioLabel.text = "音频: " + Path.GetFileName(path);
                        SetStatus("音频已选择");
                    }
                    else
                    {
                        if (_pickedAudioLabel is not null)
                            _pickedAudioLabel.text = "音频: 未选择";
                        SetStatus("音频选择失败: " + error);
                    }
                }
            }
            // 谱面文件夹导入结果轮询
            if (MobileChartPicker.Picking)
            {
                var (done, count, error) = MobileChartPicker.TryGetResult();
                if (done)
                {
                    if (error.Length == 0)
                    {
                        SetStatus($"已导入 {count} 个谱面");
                        // 若谱面浏览器仍打开，刷新列表
                        if (_dialogRoot is not null && _dialogRoot.activeInHierarchy)
                            ShowFolderBrowser();
                    }
                    else if (error == "用户取消")
                    {
                        SetStatus("已取消导入");
                    }
                    else
                    {
                        SetStatus("导入失败: " + error);
                    }
                }
            }
            // zip 导出结果轮询
            if (MobileChartPicker.Exporting)
            {
                var (done, error) = MobileChartPicker.TryGetExportResult();
                if (done)
                {
                    if (!string.IsNullOrEmpty(_pendingExportZip))
                    {
                        try { if (File.Exists(_pendingExportZip)) File.Delete(_pendingExportZip); } catch { /* ignore */ }
                    }
                    if (error.Length == 0)
                        SetStatus($"已导出: {_pendingExportName}");
                    else if (error == "用户取消")
                        SetStatus("已取消导出");
                    else
                        SetStatus("导出失败: " + error);
                    _pendingExportZip = null;
                    _pendingExportName = string.Empty;
                }
            }
#if UNITY_ANDROID
            // 诊断：仅当存在 diag.flag 文件时每 5 秒转储一次 Canvas/Camera 状态（写入 crash-mobile.log）
            if (++_diagFrame >= 300)
            {
                _diagFrame = 0;
                if (File.Exists(Path.Combine(Application.persistentDataPath, "diag.flag")))
                {
                    var sb = new StringBuilder("[MB] diag: canvases=");
                    foreach (var c in FindObjectsOfType<Canvas>(true))
                        sb.Append($"{{{c.name}|{(int)c.renderMode}|{c.sortingOrder}|{c.enabled}|{(c.gameObject.activeInHierarchy ? 1 : 0)}}},");
                    sb.Append(" cameras=");
                    foreach (var cam in Camera.allCameras)
                        sb.Append($"{{{cam.name}|{cam.rect}|{cam.depth}|{cam.enabled}}},");
                    sb.Append($" editorRoot={( _editorRoot is not null && _editorRoot.activeInHierarchy ? "alive" : "GONE")}");
                    Debug.Log(sb.ToString());
                }
            }
#endif
#endif
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
#if UNITY_ANDROID || MAJDATA_MOBILE_DEBUG
            Application.logMessageReceived -= OnUnityLog;
#endif
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
#if UNITY_ANDROID || MAJDATA_MOBILE_DEBUG
            Debug.Log($"[MB] sceneLoaded scene={scene.name} mode={mode} iid={GetInstanceID()} ready={_gameReady} done={_gameReadyDone}");
            if (scene.name == "Game" && !_gameReady)
                GameReady();
#endif
        }

        // ================= 首次启动资源解压 =================

        private IEnumerator BootstrapAndEnterGame()
        {
            var canvasGo = new GameObject("BootstrapUI", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2400);

            var panel = MakePanel(canvasGo.transform, "Panel");
            var label = MakeText(panel.transform, "MajdataX Mobile 首次初始化…", 40, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 100));
            var text = label.GetComponent<Text>();

            var ready = Directory.Exists(Path.Combine(Application.persistentDataPath, "Skin")) &&
                        Directory.Exists(Path.Combine(Application.persistentDataPath, "SFX"));
            if (!ready)
            {
                var zipPath = Path.Combine(Application.persistentDataPath, "assets.zip");
                text.text = "正在解压资源…";
                yield return null;

                using (var request = UnityEngine.Networking.UnityWebRequest.Get(Path.Combine(Application.streamingAssetsPath, "assets.zip")))
                {
                    yield return request.SendWebRequest();
                    if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        text.text = "资源读取失败: " + request.error;
                        yield break;
                    }
                    File.WriteAllBytes(zipPath, request.downloadHandler.data);
                }

                text.text = "正在解压 Skin/SFX…";
                yield return null;
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, Application.persistentDataPath, true);
                try { File.Delete(zipPath); } catch { /* ignore */ }
            }

            text.text = "初始化完成";
            yield return null;
            SceneManager.LoadScene("Game");
        }

        // ================= 游戏场景就绪：分屏 + 编辑器 =================

        private void GameReady()
        {
            if (_gameReady || _gameReadyDone) return;
            _gameReady = true;
            _gameReadyDone = true;
            Debug.Log($"[MB] GameReady begin iid={GetInstanceID()}");
            StartCoroutine(GameReadyDelayed());
        }

        /// <summary>
        /// 延迟到场景首帧之后、且渲染器管理器全部就绪后再分屏 + 构建编辑器，
        /// 避免在场景加载窗口期操作半初始化对象。
        /// </summary>
        private IEnumerator GameReadyDelayed()
        {
            yield return null; // 等首帧（PlayManager.Start / AudioManager 等全部执行）
            var waited = 0f;
            while (_audioManager is null && waited < 5f)
            {
                waited += 0.1f;
                yield return new WaitForSecondsRealtime(0.1f);
            }
            try
            {
                // 上半屏渲染器
                var cam = Camera.main;
                if (cam is not null)
                {
                    cam.rect = new Rect(0f, 1f - MobileLayout.GameAreaFraction, 1f, MobileLayout.GameAreaFraction);

                    // 等比例适配：以桌面默认 720×720（aspect=1，ortho 5.4）为设计基准。
                    // 竖屏上半屏比设计更窄时，等比放大 orthographicSize，使桌面可视宽度完整
                    // 落入视口（世界空间整体等比例缩小，画面不变形、无黑边）。
                    const float designAspect = 1f;
                    const float baseOrthoSize = 5.4f;
                    var viewportW = Screen.width * cam.rect.width;
                    var viewportH = Mathf.Max(1f, Screen.height * cam.rect.height);
                    var viewportAspect = viewportW / viewportH;
                    cam.orthographicSize = viewportAspect < designAspect
                        ? baseOrthoSize * designAspect / viewportAspect
                        : baseOrthoSize;
                    Debug.Log($"[MB] camera.rect applied, viewportAspect={viewportAspect:F3}, ortho={cam.orthographicSize:F3}");
                }
                MobileLayout.InputGated = true;

                // 移动端默认设置（无编辑器连接；应用本地设置：流速/偏移/判定音）
                try { ApplyMobileSettings(); }
                catch (Exception ex) { Debug.LogWarning($"MobileBootstrap: setting failed: {ex}"); }

                Debug.Log("[MobileBootstrap] building editor UI");
                if (_editorRoot is not null)
                {
                    Debug.LogWarning("[MobileBootstrap] editor UI already built, skipping");
                }
                else
                {
                    BuildEditorUi();
                }
                ApplyEditorBackground();
                HideGameTexts();
                SetStatus("请通过「文件 → 打开谱面」载入谱面");
                Debug.Log("[MobileBootstrap] GameReady done");

#if MAJDATA_MOBILE_DEBUG
                // 诊断：转储当前所有 UI Text 的内容与位置
                var texts = FindObjectsOfType<Text>(true);
                Debug.Log($"[MobileBootstrap] ui text count = {texts.Length}");
                foreach (var t in texts)
                {
                    if (t.text.Length == 0) continue;
                    Debug.Log($"[MobileBootstrap] TEXT '{t.text.Substring(0, Math.Min(30, t.text.Length))}' pos={t.rectTransform.anchoredPosition} parent={t.transform.parent?.name}");
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError("MobileBootstrap.GameReady failed: " + ex);
            }
        }

        private static Font CreateUiFont()
        {
            // 编辑器 UI 字体：霞鹜文楷 GB Light（用户选定，Assets/Resources/Fonts/LXGWWenKaiGB-Light.ttf）
            var font = Resources.Load<Font>("Fonts/LXGWWenKaiGB-Light");
            if (font is null)
            {
                Debug.LogWarning("[MB] LXGW font missing, fallback to system font");
                font = Font.CreateDynamicFontFromOSFont("sans-serif", 32);
            }
            else
            {
                Debug.Log("[MB] ui font: " + font.name);
            }
            font.RequestCharactersInTexture("MajdataX 文件 打开谱面 新建谱面 保存 播放 停止 难度 等级 作者 偏移 播放速度 关于 关闭 取消 确定 返回 刷新 暂无 错误 失败 已保存 正在加载 正在解析 谱面 编辑 器 请 通过 载入 选择 输入 名称 确定 提示 说明 新 谱 面 未 找到 音频 目录 支持 格式 已 全部 完成 中 下 上 左右 横 竖 屏 触摸 判定 区 域 限 制 于 等 待 检查 语法 状态 正常 空 难度 未 解析 长度 字符 行 数 清除 复制 粘贴 剪切 全选 撤销 重做 移动 光标 位置 时间 轴 轨迹 星标 特殊 皮肤 传感 滑条 开始 结束 头 部 无 法 读取 写入 权限 请 检查 设备 存储 删除 导出 导入 文件夹 创建 音频 背景 暗化 声音 偏移 流速 物量 总览 合计 窗口 秒 应用 元数据 确认 取消 恢复 默认 重置 目标 打包 平铺 视频 图片 文件 夹 空 目录 从 手机 载 入 每 行 侧 整 个 不 可 恢 复 操 作 即 写 文本 头 点 落 盘 已 打 未 尚 前 提 示 先 再 与 否 是 为 了 保 护 隐 私 该 处 置 灰 禁 用 状 态 错 误 信 息 成 功 正 在 停 止 播 放 定 位 试 玩 可 能 无 效 重 试");
            return font;
        }

        // ================= UI 构建 =================

        private void BuildEditorUi()
        {
            // 全屏画布（root 由 CanvasScaler 控制；面板作为子节点锚定到下半屏——锚点行为可靠）
            var editorCanvasGo = new GameObject("MobileEditor", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var editorCanvas = editorCanvasGo.GetComponent<Canvas>();
            editorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            editorCanvas.sortingOrder = 32000; // 置于一切 UI 之上（覆盖游戏自带画布）
            var editorScaler = editorCanvasGo.GetComponent<CanvasScaler>();
            editorScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            editorScaler.referenceResolution = new Vector2(1080, 2400);
            editorScaler.matchWidthOrHeight = 0.5f;

            _editorRoot = MakePanel(editorCanvasGo.transform, "EditorPanel");
            var editorRootRect = (RectTransform)_editorRoot.transform;
            editorRootRect.anchorMin = new Vector2(0, 0);
            editorRootRect.anchorMax = new Vector2(1, MobileLayout.GameAreaFraction);
            editorRootRect.offsetMin = Vector2.zero;
            editorRootRect.offsetMax = Vector2.zero;
            // 面板底色透明：自定义背景由子图层承载（面板 Image 保留 raycast 拦截）
            _editorRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            // 裁剪：背景图等子元素绝不越出编辑器区域（防止竖长图溢出到上半屏渲染器）
            _editorRoot.AddComponent<RectMask2D>();

            // ---- 自定义背景图（等比铺满 + 可调暗化；暗化层在背景之上、控件之下）----
            var bgGo = new GameObject("EditorBg", typeof(RectTransform), typeof(Image));
            var bgRect = (RectTransform)bgGo.transform;
            bgRect.SetParent(_editorRoot.transform, false);
            bgRect.anchorMin = new Vector2(0.5f, 0.5f);
            bgRect.anchorMax = new Vector2(0.5f, 0.5f);
            bgRect.pivot = new Vector2(0.5f, 0.5f);
            bgRect.anchoredPosition = Vector2.zero;
            var bgImage = bgGo.GetComponent<Image>();
            bgImage.color = Color.white;
            bgImage.raycastTarget = false;
            _bgRect = bgRect;
            _bgImage = bgImage;

            var dimGo = new GameObject("EditorBgDim", typeof(RectTransform), typeof(Image));
            var dimRect = (RectTransform)dimGo.transform;
            dimRect.SetParent(_editorRoot.transform, false);
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;
            var dimImage = dimGo.GetComponent<Image>();
            dimImage.color = new Color(0.078f, 0.086f, 0.102f, 0.94f); // 默认深色底
            dimImage.raycastTarget = false;
            _bgDimImage = dimImage;

            // ---- 时间轴（面板顶部整行 170px，桌面风格；内含右侧圆盘；仅自定义窗口模式）----
            _timeline = MobileTimeline.Create(_editorRoot.transform, _uiFont);
            _timeline.SetZoom(MobileTimeline.ZoomMode.WindowCustom);
            _timeline.SetCustomWindowSec(MobileSettings.Instance.TimelineWindowSec);
            _timeline.Seeked += OnTimelineSeek;
            _timeline.DragStarted += () =>
            {
                // 按下时间轴立即停止播放（按钮自动变回「播放」）
                if (_playing) _ = OnStopAsync();
            };

            // ---- 左侧竖排工具栏（文件/编辑/保存/播放）+ 难度标签 + 物量总览 ----
            // y 下移 40：时间轴已加高到 200px，避免「文件」按钮压住时间轴左下角
            var fileBtn = MakeButton(_editorRoot.transform, "文件", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(85, -258), new Vector2(150, 64));
            fileBtn.onClick.AddListener(ShowFileMenu);
            var editBtn = MakeButton(_editorRoot.transform, "编辑", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(85, -334), new Vector2(150, 64));
            editBtn.onClick.AddListener(ShowEditMenu);
            var saveBtn = MakeButton(_editorRoot.transform, "保存", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(85, -410), new Vector2(150, 64));
            saveBtn.onClick.AddListener(() => { Debug.Log("[MB] btn: save"); _ = OnSaveAsync(); });
            _playStopButton = MakeButton(_editorRoot.transform, "播放", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(85, -486), new Vector2(150, 64));
            _playStopButton.onClick.AddListener(() =>
            {
                if (_playing) { Debug.Log("[MB] btn: stop"); OnStop(); }
                else { Debug.Log($"[MB] btn: play busy={_busy} playing={_playing}"); _ = OnPlayAsync(); }
            });
            _diffLabel = MakeText(_editorRoot.transform, "难度: -", 18, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(85, -550), new Vector2(150, 56)).GetComponent<Text>();
            _diffLabel.horizontalOverflow = HorizontalWrapMode.Wrap;

            // 物量总览
            MakeText(_editorRoot.transform, "物量总览", 20, TextAnchor.MiddleCenter,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(85, -606), new Vector2(150, 44));
            var countLabels = new[] { "Tap", "Hold", "Slide", "Touch", "Break", "合计" };
            _countTexts = new Text[countLabels.Length];
            for (var i = 0; i < countLabels.Length; i++)
            {
                var label = MakeText(_editorRoot.transform, countLabels[i] + "  -", 18, TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(85, -660 - i * 40), new Vector2(150, 38));
                label.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Wrap;
                _countTexts[i] = label.GetComponent<Text>();
            }

            // ---- 实时帧数（屏幕右上角；父级为画布而非底部面板，锚定(1,1)即屏幕右上）----
            var fpsGo = MakeText(editorCanvasGo.transform, "", 22, TextAnchor.MiddleRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-90, -55), new Vector2(160, 44));
            var fpsText = fpsGo.GetComponent<Text>();
            fpsText.color = new Color(0.75f, 1f, 0.75f, 0.95f);
            fpsText.raycastTarget = false;
            var fpsShadow = fpsGo.AddComponent<Shadow>();
            fpsShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            fpsShadow.effectDistance = new Vector2(2f, -2f);
            _fpsText = fpsText;

            // ---- 时间同步读数（仅 diag.flag 存在时显示；用于定位时间轴与渲染器剩余偏差）----
            var syncGo = MakeText(editorCanvasGo.transform, "", 20, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12, -12), new Vector2(900, 34));
            var syncText = syncGo.GetComponent<Text>();
            syncText.color = new Color(1f, 0.9f, 0.3f, 0.95f);
            syncText.raycastTarget = false;
            var syncShadow = syncGo.AddComponent<Shadow>();
            syncShadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            syncShadow.effectDistance = new Vector2(1f, -1f);
            _syncReadout = syncText;

            // ---- 文本编辑区（左侧工具栏占 190px；圆盘已移入时间轴）----
            var textArea = new GameObject("TextArea", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(MobileInputField));
            var textRect = (RectTransform)textArea.transform;
            textRect.SetParent(_editorRoot.transform, false);
            textRect.anchorMin = new Vector2(0, 0);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.offsetMin = new Vector2(195, 46);
            textRect.offsetMax = new Vector2(-10, -260);
            textArea.GetComponent<Image>().color = new Color(0.102f, 0.11f, 0.129f, 0.82f); // 深色半透明：自定义背景可透出

            _textInput = textArea.GetComponent<MobileInputField>();
            _textInput.textComponent = CreateInputText(textArea.transform, out var inputTextRect);
            _textInput.lineType = InputField.LineType.MultiLineNewline;
            _textInput.characterLimit = 200000;
            _textInput.shouldHideMobileInput = true; // 键盘直输：不弹出独立输入预览框
            _textInput.text = string.Empty;
            // 移动端手势：单击放光标 / 拖动字符级光标 / 长按+拖动选择 / 纯长按整行 / 双击输入法
            _textInput.CharIndexAt = pos => CharIndexAtScreen(pos);
            _textInput.LineStartCharAt = pos => LineStartCharAtScreen(pos);
            _textInput.LineEndCharAt = pos => LineEndCharAtScreen(pos);
            _textInput.SelectionChanged = () => RefreshTextOverlays();
            _textInput.DragMoved = pos => AutoScrollDuringDrag(pos);
            _textInput.CaretPlaced = () => { RefreshTextOverlays(); CenterOnCaretLine(); };
            _textInput.onValueChanged.AddListener(_ => FitTextScroll());

            // 文本滚动条（右侧窄条 + 滑块；唯一浏览方式）
            _textScrollbar = MobileTextScrollbar.Create(textArea.transform, _uiFont);
            _textScrollbar.ValueChanged = v => ScrollTextToValue(v);

            // 自绘光标（未聚焦时的光标指示；双击 IME 会话由原生光标接管）
            var caretGo = new GameObject("CaretOverlay", typeof(RectTransform), typeof(Image));
            var caretRect = (RectTransform)caretGo.transform;
            caretRect.SetParent(inputTextRect, false);
            caretRect.anchorMin = new Vector2(0.5f, 1f);   // 锚在文本矩形顶部（= 生成器坐标原点）
            caretRect.anchorMax = new Vector2(0.5f, 1f);
            caretRect.pivot = new Vector2(0f, 1f);
            caretRect.sizeDelta = new Vector2(2f, 32f);
            var caretImage = caretGo.GetComponent<Image>();
            caretImage.color = UiSkin.AccentText; // 亮青光标：深色皮肤上清晰可辨
            caretImage.raycastTarget = false;
            caretGo.SetActive(false);
            _caretOverlay = caretGo;

            // 自绘选区（未聚焦时的选区显示；逐行条带，池化复用）
            _selOverlays = new GameObject[MaxSelectionBands];
            for (var i = 0; i < MaxSelectionBands; i++)
            {
                var selGo = new GameObject("SelBand" + i, typeof(RectTransform), typeof(Image));
                var selRect = (RectTransform)selGo.transform;
                selRect.SetParent(inputTextRect, false);
                selRect.anchorMin = new Vector2(0.5f, 1f); // 同上：顶部锚点 = 生成器坐标原点
                selRect.anchorMax = new Vector2(0.5f, 1f);
                selRect.pivot = new Vector2(0f, 1f);
                var selImage = selGo.GetComponent<Image>();
                selImage.color = UiSkin.AccentDim;
                selImage.raycastTarget = false;
                selGo.SetActive(false);
                _selOverlays[i] = selGo;
            }

            // ---- 状态栏（整行文本：中心 x=550 使 1040 宽矩形落在 30..1070）----
            _statusText = MakeText(_editorRoot.transform, "", 22, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(550, 24), new Vector2(1040, 44)).GetComponent<Text>();
            _statusText.color = UiSkin.TextSecondary;

            // ---- 对话框容器（文件菜单 / 谱面浏览器）----
            var dialogGo = new GameObject("Dialog", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var dialogCanvas = dialogGo.GetComponent<Canvas>();
            dialogCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            dialogCanvas.sortingOrder = 32001;
            var dialogScaler = dialogGo.GetComponent<CanvasScaler>();
            dialogScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            dialogScaler.referenceResolution = new Vector2(1080, 2400);
            dialogScaler.matchWidthOrHeight = 0.5f;
            _dialogRoot = dialogGo;
            _dialogPanel = MakePanel(dialogGo.transform, "DialogPanel");
            _dialogRoot.SetActive(false);

            DontDestroyOnLoad(editorCanvasGo);
            DontDestroyOnLoad(dialogGo);
        }

        /// <summary>输入框文本（uGUI Text + 同一动态字体；避免运行时创建额外字体资源）。</summary>
        /// <remarks>
        /// 关键：文本矩形顶部锚定、高度随内容增长（"高矩形"）。
        /// InputField 的 UpdateLabel 即使未聚焦也会把绘制范围截断到 rect 高度一屏，
        /// 只有让 rect 高度 ≥ 内容高度，未聚焦时才绘制全文（滚动由移动矩形实现，RectMask2D 裁剪）。
        /// </remarks>
        private Text CreateInputText(Transform parent, out RectTransform rect)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(16f, -900f);
            rect.offsetMax = new Vector2(-45f, 0f); // 右缘留白加大：避开 35px 宽滚动条
            var text = go.AddComponent<Text>();
            text.font = _uiFont;
            text.fontSize = 26;
            text.color = UiSkin.TextPrimary;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = true;
            return text;
        }

        private static GameObject MakePanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var bg = go.AddComponent<Image>();
            bg.color = UiSkin.PanelBg;
            return go;
        }

        private GameObject MakeText(Transform parent, string content, int fontSize, TextAnchor anchor,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2? size = null)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            SetRect(rect, anchorMin, anchorMax, anchoredPos, size ?? new Vector2(1000, 80));
            var text = go.AddComponent<Text>();
            text.font = _uiFont;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = UiSkin.TextPrimary;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false; // 文本一律不拦截点击
            text.text = content;
            return go;
        }

        private Button MakeButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPos, Vector2 size)
        {
            UiSkin.EnsureCreated();
            var go = new GameObject("Button", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            SetRect(rect, anchorMin, anchorMax, anchoredPos, size);
            var image = go.AddComponent<Image>();
            image.sprite = UiSkin.Rounded;
            image.type = Image.Type.Sliced;
            image.color = Color.white; // 底色全部交给 ColorBlock（Image.color 与 normalColor 会相乘）
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = UiSkin.ButtonBg;
            colors.highlightedColor = UiSkin.ButtonHover;
            colors.pressedColor = UiSkin.ButtonPressed;
            colors.selectedColor = UiSkin.ButtonBg; // 点击后不保持选中高亮（修复播放按钮发白不淡出）
            colors.disabledColor = UiSkin.ButtonDisabled;
            button.colors = colors;
            var labelGo = MakeText(go.transform, label, 26, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            labelGo.GetComponent<Text>().raycastTarget = false;
            UiSkin.AttachPressGlow(button); // 按压发光（子层在标签之下）
            return button;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
        }

        private static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

        private void SetStatus(string message)
        {
            if (_statusText is not null)
                _statusText.text = message;
            Debug.Log("[MB] status: " + message);
        }

        // ================= 文件菜单 / 对话框 =================

        private void ShowFileMenu()
        {
            if (_dialogRoot is null || _dialogPanel is null) return;
            _dialogRoot.SetActive(true);
            ClearChildren(_dialogPanel.transform);

            MakeText(_dialogPanel.transform, "文件", 40, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(200, 90));

            var openBtn = MakeButton(_dialogPanel.transform, "打开谱面…", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -140), new Vector2(760, 84));
            openBtn.onClick.AddListener(ShowFolderBrowser);
            var newBtn = MakeButton(_dialogPanel.transform, "新建谱面…", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -240), new Vector2(760, 84));
            newBtn.onClick.AddListener(ShowNewChartDialog);

            // 难度
            MakeText(_dialogPanel.transform, "难度", 28, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-330, -340), new Vector2(200, 60));
            for (var i = 0; i < 8; i++)
            {
                var col = i % 4;
                var row = i / 4;
                var idx = i;
                var btn = MakeButton(_dialogPanel.transform, DiffNames[i], new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-270 + col * 190, -400 - row * 96), new Vector2(170, 80));
                if (i == _difficulty)
                    UiSkin.ApplyAccent(btn);
                btn.onClick.AddListener(() =>
                {
                    // 先提交当前块，再切换到目标难度并刷新块视图
                    CommitEditorBlock();
                    _difficulty = idx;
                    SetEditorBlockView(_fileText, _difficulty);
                    ShowFileMenu();
                    UpdateDiffLabel();
                });
            }

            // 等级 / 作者 / 偏移（输入即写入谱面文本头部，点「保存」落盘）
            var levelInput = MakeTextField(_dialogPanel.transform, "等级", -620, GetMetaValue("lv"));
            levelInput.onValueChanged.AddListener(v => WriteMetaToFile("lv", v));
            var designerInput = MakeTextField(_dialogPanel.transform, "作者", -720, GetMetaValue("des"));
            designerInput.onValueChanged.AddListener(v => WriteMetaToFile("des", v));
            var offsetInput = MakeTextField(_dialogPanel.transform, "偏移", -820, GetMetaValue("first"));
            offsetInput.onValueChanged.AddListener(v => WriteMetaToFile("first", v));

            // 播放速度
            MakeText(_dialogPanel.transform, "播放速度", 28, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-330, -920), new Vector2(300, 60));
            for (var i = 0; i < 8; i++)
            {
                var sp = 0.5f + i * 0.25f;
                var btn = MakeButton(_dialogPanel.transform, sp.ToString("0.##"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-270 + (i % 4) * 190, -980 - (i / 4) * 96), new Vector2(170, 80));
                if (Mathf.Approximately(sp, _speed))
                    UiSkin.ApplyAccent(btn);
                btn.onClick.AddListener(() =>
                {
                    _speed = sp;
                    ShowFileMenu();
                });
            }

            var exportBtn = MakeButton(_dialogPanel.transform, "导出 zip", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-240, 60), new Vector2(240, 84));
            exportBtn.onClick.AddListener(ShowExportConfirmOrStart);
            var settingsBtn = MakeButton(_dialogPanel.transform, "设置", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-50, 60), new Vector2(140, 84));
            settingsBtn.onClick.AddListener(ShowSettingsDialog);
            var closeBtn = MakeButton(_dialogPanel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(160, 60), new Vector2(240, 84));
            closeBtn.onClick.AddListener(() => _dialogRoot.SetActive(false));
        }

        private static readonly string[] EditOpNames =
        {
            "左右翻转选择", "上下翻转选择", "180°翻转选择", "45°顺时针旋转选择",
            "45°逆时针旋转选择", "将乐句1.5倍细分", "将乐句2倍细分"
        };

        /// <summary>编辑菜单：对选中文本应用原生 MajdataEdit-Neo 的 7 项变换。</summary>
        private void ShowEditMenu()
        {
            if (_dialogRoot is null || _dialogPanel is null) return;
            _dialogRoot.SetActive(true);
            ClearChildren(_dialogPanel.transform);

            MakeText(_dialogPanel.transform, "编辑", 40, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(200, 90));
            MakeText(_dialogPanel.transform, "对选中文本进行变换（长按拖动选择，纯长按选整行）", 24, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(760, 50));

            for (var i = 0; i < EditOpNames.Length; i++)
            {
                var op = (MobileSimaiEdit.EditOp)i;
                var name = EditOpNames[i];
                var btn = MakeButton(_dialogPanel.transform, name, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0, -190 - i * 92), new Vector2(760, 80));
                btn.onClick.AddListener(() =>
                {
                    ApplyEditOp(op, name);
                    _dialogRoot.SetActive(false);
                });
            }

            var closeBtn = MakeButton(_dialogPanel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 60), new Vector2(240, 84));
            closeBtn.onClick.AddListener(() => _dialogRoot.SetActive(false));
        }

        /// <summary>把变换应用到当前选区，并保持变换后区域为选中状态（便于连续变换）。</summary>
        private void ApplyEditOp(MobileSimaiEdit.EditOp op, string name)
        {
            if (_textInput is null) return;
            var text = _textInput.text;
            var a = Mathf.Min(_textInput.selectionAnchorPosition, _textInput.selectionFocusPosition);
            var f = Mathf.Max(_textInput.selectionAnchorPosition, _textInput.selectionFocusPosition);
            if (f <= a || f > text.Length || a < 0)
            {
                SetStatus("请先选择文本（长按拖动选择，或纯长按选整行）");
                return;
            }
            var sel = text.Substring(a, f - a);
            var transformed = MobileSimaiEdit.Apply(sel, op);
            var newText = text.Substring(0, a) + transformed + text.Substring(f);
            _textInput.text = newText;
            // 变换后重新选中该区域（供连续应用多个变换）
            _textInput.selectionAnchorPosition = a;
            _textInput.selectionFocusPosition = a + transformed.Length;
            RebuildBlockLineIndex();
            Canvas.ForceUpdateCanvases();
            RefreshTextOverlays();
            SetStatus($"已应用: {name}（记得保存）");
        }

        /// <summary>文本内容变化后重建块视图行索引（行数不变时仅重算字符起点）。</summary>
        private void RebuildBlockLineIndex()
        {
            if (_textInput is null) return;
            var displayed = _textInput.text.Split('\n');
            var starts = new int[displayed.Length];
            var pos = 0;
            for (var i = 0; i < displayed.Length; i++)
            {
                starts[i] = pos;
                pos += displayed[i].Length + 1;
            }
            _blockLineCharStarts = starts;
        }

        private InputField MakeTextField(Transform parent, string labelText, float y, string value)
        {
            MakeText(parent, labelText, 26, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-330, y), new Vector2(200, 60));
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            SetRect(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(60, y), new Vector2(560, 66));
            UiSkin.EnsureCreated();
            var inputBg = go.GetComponent<Image>();
            inputBg.sprite = UiSkin.Rounded;
            inputBg.type = Image.Type.Sliced;
            inputBg.color = UiSkin.InputBg;
            var input = go.GetComponent<InputField>();
            input.textComponent = CreateInputText(go.transform, out _);
            input.lineType = InputField.LineType.SingleLine;
            input.text = value;
            return input;
        }

        private void ShowFolderBrowser()
        {
            if (_dialogRoot is null || _dialogPanel is null) return;
            _dialogRoot.SetActive(true);
            ClearChildren(_dialogPanel.transform);

            MakeText(_dialogPanel.transform, "选择谱面", 40, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(300, 90));
            MakeText(_dialogPanel.transform, $"目录: {ChartsRoot}", 22, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -95), new Vector2(1000, 50));

            Directory.CreateDirectory(ChartsRoot);
            var dirs = Directory.GetDirectories(ChartsRoot);
            var entries = new List<(string dir, string title, string designer)>();
            foreach (var dir in dirs)
            {
                var maidata = Path.Combine(dir, "maidata.txt");
                if (!File.Exists(maidata)) continue;
                var meta = ReadMeta(maidata);
                entries.Add((dir, meta.title, meta.designer));
            }
            entries.Sort((a, b) => string.Compare(a.title, b.title, StringComparison.Ordinal));

            var y = -170f;
            if (entries.Count == 0)
            {
                MakeText(_dialogPanel.transform, "（暂无谱面，点下方「导入谱面文件夹…」从手机存储导入）", 26, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 420), new Vector2(1000, 120));
            }
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry.dir);
                var label = entry.designer.Length > 0
                    ? $"{entry.title}  [{name}]  by {entry.designer}"
                    : $"{entry.title}  [{name}]";
                var btn = MakeButton(_dialogPanel.transform, label, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-80, y), new Vector2(840, 96));
                var dirCapture = entry.dir;
                btn.onClick.AddListener(() => _ = OnOpenChartAsync(dirCapture));
                var delBtn = MakeButton(_dialogPanel.transform, "删除", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(410, y), new Vector2(140, 96));
                UiSkin.ApplyDanger(delBtn);
                delBtn.onClick.AddListener(() => ShowDeleteChartConfirm(dirCapture));
                y -= 108;
            }

            // 导入：SAF 文件夹选择器，把手机存储中的谱面文件夹复制进 Charts 目录
            var importBtn = MakeButton(_dialogPanel.transform, "导入谱面文件夹…", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-300, 40), new Vector2(400, 84));
            importBtn.onClick.AddListener(() =>
            {
                Directory.CreateDirectory(ChartsRoot);
                if (!MobileChartPicker.StartPick(ChartsRoot))
                    SetStatus("无法打开系统文件夹选择器");
            });
            var backBtn = MakeButton(_dialogPanel.transform, "返回", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(300, 40), new Vector2(240, 84));
            backBtn.onClick.AddListener(ShowFileMenu);
        }

        /// <summary>删除确认弹窗：删除整个谱面文件夹（maidata + 音频 + 图片等），不可恢复。</summary>
        private void ShowDeleteChartConfirm(string dir)
        {
            if (_dialogRoot is null || _dialogPanel is null) return;
            _dialogRoot.SetActive(true);
            ClearChildren(_dialogPanel.transform);

            var name = Path.GetFileName(dir);
            MakeText(_dialogPanel.transform, "删除谱面", 40, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(300, 90));
            MakeText(_dialogPanel.transform, $"[{name}]", 30, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -160), new Vector2(900, 70));
            MakeText(_dialogPanel.transform, "将删除整个谱面文件夹（maidata.txt、音频、图片等全部内容），此操作不可恢复。", 26, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -260), new Vector2(920, 120));

            var delBtn = MakeButton(_dialogPanel.transform, "删除", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-160, 60), new Vector2(300, 84));
            UiSkin.ApplyDanger(delBtn, true);
            var dirCapture = dir;
            delBtn.onClick.AddListener(() => _ = DeleteChartAsync(dirCapture));
            var cancelBtn = MakeButton(_dialogPanel.transform, "取消", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(160, 60), new Vector2(240, 84));
            cancelBtn.onClick.AddListener(ShowFolderBrowser);
        }

        /// <summary>删除谱面文件夹；若为当前打开的谱面，编辑器与渲染器回到空状态。</summary>
        private async UniTaskVoid DeleteChartAsync(string dir)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var name = Path.GetFileName(dir);
                var wasOpen = string.Equals(_currentFolder, dir, StringComparison.Ordinal);
                if (_playing)
                {
                    try { await _playManager.StopAsync(); } catch { /* ignore */ }
                    _playing = false;
                    if (_playStopButton is not null)
                        _playStopButton.GetComponentInChildren<Text>().text = "播放";
                }
                Directory.Delete(dir, true);

                if (wasOpen)
                {
                    // 清空编辑器与渲染器状态，回到未打开谱面时的空态
                    _currentFolder = string.Empty;
                    _fileText = string.Empty;
                    if (_textInput is not null) _textInput.text = string.Empty;
                    _difficulty = 0;
                    _parsedFile = null;
                    _parsedChart = SimaiChart.Empty;
                    _chartOffset = 0d;
                    _pendingStart = 0d;
                    _lastWaveTrack = string.Empty;
                    _lastWaveform = null;
                    _timeline?.SetChart(SimaiChart.Empty);
                    BgManager.hasBg = false;
                    BgManager.hasVideo = false;
                    _bgManager?.ShowDefaultBackground();
                    await _playManager.LoadChartAsync(SimaiFile.Empty(string.Empty, string.Empty), SimaiChart.Empty, 0);
                    UpdateNoteCounts();
                    UpdateDiffLabel();
                }
                SetStatus($"已删除: {name}");
                ShowFolderBrowser();
            }
            catch (Exception ex)
            {
                SetStatus("删除失败: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private void ShowNewChartDialog()
        {
            if (_dialogRoot is null || _dialogPanel is null) return;
            _dialogRoot.SetActive(true);
            ClearChildren(_dialogPanel.transform);

            MakeText(_dialogPanel.transform, "新建谱面", 40, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(300, 90));
            var nameInput = MakeTextField(_dialogPanel.transform, "名称", -180, "");

            // 音频选择（必选，走系统 SAF 选择器）
            var pickBtn = MakeButton(_dialogPanel.transform, "选择音频…", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-250, -300), new Vector2(300, 84));
            pickBtn.onClick.AddListener(OnPickAudioClicked);
            _pickedAudioLabel = MakeText(_dialogPanel.transform,
                _stagedAudioPath is null ? "音频: 未选择" : "音频: " + Path.GetFileName(_stagedAudioPath),
                24, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(150, -300), new Vector2(600, 84)).GetComponent<Text>();

            var okBtn = MakeButton(_dialogPanel.transform, "创建", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-160, -420), new Vector2(300, 84));
            okBtn.onClick.AddListener(() => _ = OnCreateChartAsync(nameInput.text));
            var cancelBtn = MakeButton(_dialogPanel.transform, "取消", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(180, -420), new Vector2(240, 84));
            cancelBtn.onClick.AddListener(ShowFileMenu);
        }

        // ================= 设置对话框（批次C）=================

        /// <summary>应用本地设置到渲染器（流速 / 全局偏移 / 判定音音量）。</summary>
        private void ApplyMobileSettings()
        {
            var s = MobileSettings.Instance;
            // 判定音音量：与原生 MajdataEdit-Neo 相同的 9 类（0~100%，默认 90%）
            _volumeSetting.Track = s.TrackVol / 100f;
            _volumeSetting.Answer = s.AnswerVol / 100f;
            _volumeSetting.Tap = s.TapVol / 100f;
            _volumeSetting.Slide = s.SlideVol / 100f;
            _volumeSetting.Break = s.BreakVol / 100f;
            _volumeSetting.BreakSlide = s.BreakSlideVol / 100f;
            _volumeSetting.Ex = s.ExVol / 100f;
            _volumeSetting.Touch = s.TouchVol / 100f;
            _volumeSetting.Hanabi = s.HanabiVol / 100f;
            _playManager.Setting(new MajViewSetting
            {
                TapSpeed = s.TapSpeed,
                TouchSpeed = s.TouchSpeed,
                GlobalAudioOffset = s.GlobalOffsetMs / 1000.0,
            }, _volumeSetting);
        }

        private void ShowSettingsDialog()
        {
            if (_dialogRoot is null || _dialogPanel is null) return;
            _dialogRoot.SetActive(true);
            ClearChildren(_dialogPanel.transform);

            MakeText(_dialogPanel.transform, "设置（仅对本应用生效，不写入谱面）", 34, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(920, 70));

            var y = -110f;
            const float rowH = 92f;

            // ---- 流速 / 偏移滑条 ----
            var tapSlider = MakeSlider(_dialogPanel.transform, new Vector2(80, y), 1f, 10f, MobileSettings.Instance.TapSpeed, v =>
            {
                MobileSettings.Instance.TapSpeed = v;
                MobileSettings.Instance.Save();
                ApplyMobileSettings();
                _tapValueLabel.text = v.ToString("0.0");
            });
            MakeText(_dialogPanel.transform, "Tap 流速", 26, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-430, y), new Vector2(220, 60));
            _tapValueLabel = MakeText(_dialogPanel.transform, MobileSettings.Instance.TapSpeed.ToString("0.0"), 24, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(430, y), new Vector2(140, 60)).GetComponent<Text>();
            y -= rowH;

            var touchSlider = MakeSlider(_dialogPanel.transform, new Vector2(80, y), 1f, 10f, MobileSettings.Instance.TouchSpeed, v =>
            {
                MobileSettings.Instance.TouchSpeed = v;
                MobileSettings.Instance.Save();
                ApplyMobileSettings();
                _touchValueLabel.text = v.ToString("0.0");
            });
            MakeText(_dialogPanel.transform, "Touch 流速", 26, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-430, y), new Vector2(220, 60));
            _touchValueLabel = MakeText(_dialogPanel.transform, MobileSettings.Instance.TouchSpeed.ToString("0.0"), 24, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(430, y), new Vector2(140, 60)).GetComponent<Text>();
            y -= rowH;

            var offsetSlider = MakeSlider(_dialogPanel.transform, new Vector2(80, y), -300f, 300f, (float)MobileSettings.Instance.GlobalOffsetMs, v =>
            {
                MobileSettings.Instance.GlobalOffsetMs = Math.Round(v);
                MobileSettings.Instance.Save();
                ApplyMobileSettings();
                _offsetValueLabel.text = MobileSettings.Instance.GlobalOffsetMs.ToString("0") + " ms";
            });
            MakeText(_dialogPanel.transform, "全局声音偏移", 26, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-430, y), new Vector2(220, 60));
            _offsetValueLabel = MakeText(_dialogPanel.transform, MobileSettings.Instance.GlobalOffsetMs.ToString("0") + " ms", 24, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(430, y), new Vector2(180, 60)).GetComponent<Text>();
            y -= rowH;

            // ---- 时间轴窗口（仅自定义模式：0.25~8 秒滑条，即时生效）----
            MakeText(_dialogPanel.transform, "时间轴窗口", 26, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-430, y), new Vector2(220, 60));
            MakeSlider(_dialogPanel.transform, new Vector2(80, y), 0.25f, 8f, MobileSettings.Instance.TimelineWindowSec, v =>
            {
                MobileSettings.Instance.TimelineWindowSec = v;
                MobileSettings.Instance.Save();
                _timeline?.SetZoom(MobileTimeline.ZoomMode.WindowCustom);
                _timeline?.SetCustomWindowSec(v);
                if (_winValueLabel is not null)
                    _winValueLabel.text = v.ToString("0.0") + " 秒";
            });
            _winValueLabel = MakeText(_dialogPanel.transform, MobileSettings.Instance.TimelineWindowSec.ToString("0.0") + " 秒", 24, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(430, y), new Vector2(140, 60)).GetComponent<Text>();
            y -= rowH;

            // ---- 编辑器背景 ----
            MakeText(_dialogPanel.transform, "编辑器背景", 26, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-430, y), new Vector2(220, 60));
            var bgPickBtn = MakeButton(_dialogPanel.transform, "选择图片", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-120, y), new Vector2(200, 64));
            bgPickBtn.onClick.AddListener(PickBackground);
            var bgClearBtn = MakeButton(_dialogPanel.transform, "清除", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(110, y), new Vector2(130, 64));
            bgClearBtn.onClick.AddListener(ClearBackground);
            y -= rowH;

            var dimSlider = MakeSlider(_dialogPanel.transform, new Vector2(80, y), 0f, 0.85f, MobileSettings.Instance.BackgroundDim, v =>
            {
                MobileSettings.Instance.BackgroundDim = v;
                MobileSettings.Instance.Save();
                ApplyEditorBackground();
                if (_bgDimValueLabel is not null)
                    _bgDimValueLabel.text = Mathf.RoundToInt(v * 100) + "%";
            });
            MakeText(_dialogPanel.transform, "背景暗化", 26, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-430, y), new Vector2(220, 60));
            _bgDimValueLabel = MakeText(_dialogPanel.transform, Mathf.RoundToInt(MobileSettings.Instance.BackgroundDim * 100) + "%", 24, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(430, y), new Vector2(140, 60)).GetComponent<Text>();
            y -= rowH + 10;

            MakeText(_dialogPanel.transform, "判定音音量", 26, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(700, 50));
            y -= 62f;

            // ---- 判定音音量（与原生 MajdataEdit-Neo 一致：9 类，0~100%，默认 90%）----
            var volCats = new List<(string label, Func<float> get, Action<float> set)>
            {
                ("音乐 Track", () => MobileSettings.Instance.TrackVol, v => MobileSettings.Instance.TrackVol = v),
                ("应答 Answer", () => MobileSettings.Instance.AnswerVol, v => MobileSettings.Instance.AnswerVol = v),
                ("Tap", () => MobileSettings.Instance.TapVol, v => MobileSettings.Instance.TapVol = v),
                ("Slide", () => MobileSettings.Instance.SlideVol, v => MobileSettings.Instance.SlideVol = v),
                ("Break", () => MobileSettings.Instance.BreakVol, v => MobileSettings.Instance.BreakVol = v),
                ("BreakSlide", () => MobileSettings.Instance.BreakSlideVol, v => MobileSettings.Instance.BreakSlideVol = v),
                ("Ex", () => MobileSettings.Instance.ExVol, v => MobileSettings.Instance.ExVol = v),
                ("Touch", () => MobileSettings.Instance.TouchVol, v => MobileSettings.Instance.TouchVol = v),
                ("花火 Hanabi", () => MobileSettings.Instance.HanabiVol, v => MobileSettings.Instance.HanabiVol = v),
            };
            _sfxVolLabels = new Text[volCats.Count];
            for (var i = 0; i < volCats.Count; i++)
            {
                var (label, get, set) = volCats[i];
                var col = i % 2;
                var rowY = y - (i / 2) * 76f;
                var off = (col == 0 ? 270f : 800f) - 540f;
                var idxCapture = i;
                MakeText(_dialogPanel.transform, label, 19, TextAnchor.MiddleLeft,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(off - 200, rowY), new Vector2(120, 56));
                var volSlider = MakeSlider(_dialogPanel.transform, new Vector2(off + 40, rowY), 0f, 100f, get(), v =>
                {
                    set(v);
                    MobileSettings.Instance.Save();
                    ApplyMobileSettings();
                    if (idxCapture < _sfxVolLabels.Length && _sfxVolLabels[idxCapture] is not null)
                        _sfxVolLabels[idxCapture].text = Mathf.RoundToInt(v) + "%";
                });
                volSlider.GetComponent<RectTransform>().sizeDelta = new Vector2(150, 40);
                _sfxVolLabels[i] = MakeText(_dialogPanel.transform, Mathf.RoundToInt(get()) + "%", 17, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(off + 160, rowY), new Vector2(60, 56)).GetComponent<Text>();
            }

            var backBtn = MakeButton(_dialogPanel.transform, "返回", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 60), new Vector2(240, 84));
            backBtn.onClick.AddListener(ShowFileMenu);
        }

        private Text? _tapValueLabel;
        private Text? _touchValueLabel;
        private Text? _offsetValueLabel;
        private Text? _winValueLabel;
        private Text[] _sfxVolLabels = Array.Empty<Text>();

        private Slider MakeSlider(Transform parent, Vector2 anchoredPos, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChanged)
        {
            var go = new GameObject("Slider", typeof(RectTransform), typeof(MobileSlider));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            SetRect(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), anchoredPos, new Vector2(520, 44));
            var slider = go.GetComponent<MobileSlider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = false;

            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            var bgRect = (RectTransform)bgGo.transform;
            bgRect.SetParent(rect, false);
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = new Vector2(0, 16);
            bgRect.offsetMax = new Vector2(0, -16);
            var bgImage = bgGo.GetComponent<Image>();
            UiSkin.EnsureCreated();
            bgImage.sprite = UiSkin.Rounded;
            bgImage.type = Image.Type.Sliced;
            bgImage.color = UiSkin.TrackBg;
            bgImage.raycastTarget = true; // 滑条主命中区

            var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
            var fillArea = (RectTransform)fillAreaGo.transform;
            fillArea.SetParent(rect, false);
            fillArea.anchorMin = Vector2.zero;
            fillArea.anchorMax = Vector2.one;
            fillArea.offsetMin = new Vector2(10, 16);
            fillArea.offsetMax = new Vector2(-10, -16);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.SetParent(fillArea, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = new Vector2(0, 0);
            fillRect.offsetMax = new Vector2(0, 0);
            var fillImage = fillGo.GetComponent<Image>();
            fillImage.color = new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.9f);
            fillImage.raycastTarget = false;

            var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
            var handleArea = (RectTransform)handleAreaGo.transform;
            handleArea.SetParent(rect, false);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(10, 0);
            handleArea.offsetMax = new Vector2(-10, 0);

            // 不设 handleRect：整个滑条可点击/拖动定位（填充条右缘即指示）
            slider.fillRect = fillRect;
            slider.targetGraphic = bgImage;
            if (onChanged is not null) slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        private void OnPickAudioClicked()
        {
            var staging = Path.Combine(ChartsRoot, ".new_tmp");
            try { Directory.CreateDirectory(staging); }
            catch (Exception ex) { SetStatus("创建临时目录失败: " + ex.Message); return; }
            if (!MobileAudioPicker.StartPick(staging))
                SetStatus("无法打开系统音频选择器");
            else
                SetStatus("请在系统选择器中选择音频文件…");
        }

        // ================= 按难度分块编辑（批次B）=================

        /// <summary>定位当前难度 inote 块：返回 (起始行, 结束行(不含), 头部行剩余内容)。</summary>
        private (int start, int endExcl, string headerRemainder) ScanInoteBlock(string fullText, int diffIdx)
        {
            var lines = fullText.Split('\n');
            var target = "&inote_" + (diffIdx + 1) + "=";
            var start = -1;
            var endExcl = lines.Length;
            var remainder = string.Empty;
            for (var i = 0; i < lines.Length; i++)
            {
                var t = lines[i].TrimStart();
                if (t.StartsWith("&", StringComparison.Ordinal))
                {
                    if (start < 0 && t.StartsWith(target, StringComparison.Ordinal))
                    {
                        start = i;
                        var eq = lines[i].IndexOf('=');
                        if (eq >= 0) remainder = lines[i].Substring(eq + 1).TrimStart();
                    }
                    else if (start >= 0) { endExcl = i; break; }
                }
            }
            if (start < 0) { start = lines.Length; endExcl = lines.Length; }
            return (start, endExcl, remainder);
        }

        /// <summary>把编辑器内容切换为指定难度的 inote 块视图（元数据与其他难度由文件菜单维护）。</summary>
        private void SetEditorBlockView(string fullText, int diffIdx)
        {
            _fileText = fullText.Replace("\r\n", "\n");
            if (_textInput is null) return;
            var (start, endExcl, remainder) = ScanInoteBlock(_fileText, diffIdx);
            var allLines = _fileText.Split('\n');
            var fullIndices = new List<int>();
            var displayed = new List<string>();
            if (remainder.Length > 0)
            {
                fullIndices.Add(start);
                displayed.Add(remainder);
            }
            for (var i = start + 1; i < endExcl; i++)
            {
                fullIndices.Add(i);
                displayed.Add(allLines[i]);
            }
            _textInput.text = string.Join("\n", displayed);
            // 切换难度后回到顶部
            _textInput.textComponent.rectTransform.anchoredPosition = Vector2.zero;
            SyncTextScrollbar();
            var starts = new int[displayed.Count];
            var pos = 0;
            for (var i = 0; i < displayed.Count; i++)
            {
                starts[i] = pos;
                pos += displayed[i].Length + 1;
            }
            _blockLineFullIndices = fullIndices.ToArray();
            _blockLineCharStarts = starts;
        }

        /// <summary>把编辑器当前块内容写回完整文件（切换难度/保存/播放前调用）。</summary>
        private void CommitEditorBlock()
        {
            if (_textInput is null || string.IsNullOrEmpty(_fileText)) return;
            var edited = _textInput.text.Replace("\r\n", "\n");
            var lines = _fileText.Split('\n');
            var (start, endExcl, _) = ScanInoteBlock(_fileText, _difficulty);
            var editedLines = edited.Split('\n');
            var rebuilt = new List<string>(lines.Length + editedLines.Length + 1);
            for (var i = 0; i < start; i++) rebuilt.Add(lines[i]);
            rebuilt.Add("&inote_" + (_difficulty + 1) + "=");
            foreach (var l in editedLines) rebuilt.Add(l);
            for (var i = endExcl; i < lines.Length; i++) rebuilt.Add(lines[i]);
            var joined = string.Join("\n", rebuilt);
            if (!joined.EndsWith("\n", StringComparison.Ordinal)) joined += "\n";
            _fileText = joined;
        }

        /// <summary>文本框视图高度（文本矩形父级高 − 上下边距 10px）。</summary>
        private float GetViewHeight()
        {
            if (_textInput is null) return 874f;
            var parent = _textInput.textComponent.rectTransform.parent as RectTransform;
            var h = parent is not null ? parent.rect.height : 874f;
            return Mathf.Max(40f, h - 20f);
        }

        /// <summary>用独立生成器量测整文布局高度（与 Text 组件当前网格/输入框截断状态无关）。</summary>
        private float MeasureContentHeight(string content)
        {
            if (_textInput is null) return 874f;
            var text = _textInput.textComponent;
            var parent = text.rectTransform.parent as RectTransform;
            var w = Mathf.Max(50f, (parent is not null ? parent.rect.width : 1060f) - 32f);
            var settings = text.GetGenerationSettings(new Vector2(w, 0f));
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.horizontalOverflow = text.horizontalOverflow;
            settings.generateOutOfBounds = true;
            var gen = new TextGenerator();
            gen.PopulateWithErrors(content, settings, _textInput.gameObject);
            if (gen.lineCount <= 0)
                return Mathf.Max(10f, text.fontSize * 1.15f);
            var first = gen.lines[0];
            var last = gen.lines[gen.lineCount - 1];
            return Mathf.Max(Mathf.Max(10f, text.fontSize * 1.15f), first.topY - last.topY + last.height);
        }

        /// <summary>实际行高（相邻两行 topY 差；生成器不可用时按字号估算）。</summary>
        private float GetLineHeight()
        {
            if (_textInput is null) return 29.9f;
            var text = _textInput.textComponent;
            var gen = text.cachedTextGenerator;
            if (gen.lineCount >= 2)
                return Mathf.Max(10f, gen.lines[0].topY - gen.lines[1].topY);
            if (gen.lineCount == 1)
                return Mathf.Max(10f, gen.lines[0].height);
            return Mathf.Max(10f, text.fontSize * 1.15f);
        }

        /// <summary>文本变化后：把文本矩形高度适配到内容高度（"高矩形"，防止 InputField 截断绘制），并钳制滚动。</summary>
        private void FitTextScroll()
        {
            if (_textInput is null) return;
            var textRect = _textInput.textComponent.rectTransform;
            var viewH = GetViewHeight();
            var contentH = Mathf.Max(viewH, MeasureContentHeight(_textInput.text)); // 独立量测：不受 onValueChanged 时序影响
            if (Mathf.Abs(textRect.rect.height - contentH) > 0.5f)
            {
                textRect.offsetMin = new Vector2(textRect.offsetMin.x, -contentH);
                textRect.offsetMax = new Vector2(textRect.offsetMax.x, 0f);
            }
            var max = Mathf.Max(0f, contentH - viewH);
            var y = Mathf.Clamp(textRect.anchoredPosition.y, 0f, max);
            if (!Mathf.Approximately(y, textRect.anchoredPosition.y))
                textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, y);
            SyncTextScrollbar();
            Debug.Log($"[MB] fit contentH={contentH:F0} viewH={viewH:F0} len={_textInput.text.Length}");
        }

        /// <summary>滚动到指定比例（0=顶，1=底；滚动条回调 = 唯一浏览方式）。</summary>
        private void ScrollTextToValue(float v)
        {
            if (_textInput is null) return;
            // 滚动条接管浏览：先解除编辑焦点（其内部截断式窗口会与整文滚动冲突）
            if (EventSystem.current is not null &&
                EventSystem.current.currentSelectedGameObject == _textInput.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            Canvas.ForceUpdateCanvases(); // 让 Text 网格/生成器立即反映整文
            var textRect = _textInput.textComponent.rectTransform;
            var viewH = GetViewHeight();
            var contentH = Mathf.Max(viewH, textRect.rect.height);
            var max = Mathf.Max(0f, contentH - viewH);
            var y = Mathf.Clamp01(v) * max;
            textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, y);
            SyncTextScrollbar();
            Debug.Log($"[MB] scroll v={v:F3} contentH={contentH:F0} viewH={viewH:F0} y={y:F0} lines={_blockLineCharStarts.Length}");
        }

        /// <summary>由当前文本偏移反推并同步滚动条数值。</summary>
        private void SyncTextScrollbar()
        {
            if (_textInput is null || _textScrollbar is null) return;
            var textRect = _textInput.textComponent.rectTransform;
            var viewH = GetViewHeight();
            var contentH = Mathf.Max(viewH, textRect.rect.height);
            var max = Mathf.Max(0f, contentH - viewH);
            _textScrollbar.Value = max <= 0f ? 0f : textRect.anchoredPosition.y / max;
        }

        /// <summary>块视图字符索引 → 块视图行号（二分；用于光标行居中）。</summary>
        private int LineOfCharInBlock(int charIdx)
        {
            if (charIdx < 0 || _blockLineCharStarts.Length == 0) return -1;
            var lo = 0;
            var hi = _blockLineCharStarts.Length - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (_blockLineCharStarts[mid] <= charIdx) lo = mid; else hi = mid - 1;
            }
            return _blockLineCharStarts[lo] <= charIdx ? lo : -1;
        }

        /// <summary>编辑态光标落位后：把光标所在行滚动到视图中央（含绘制窗口偏移补偿）。</summary>
        private void CenterOnCaretLine()
        {
            if (_textInput is null) return;
            var caretLine = LineOfCharInBlock(_textInput.caretPosition);
            if (caretLine < 0) return;
            var text = _textInput.textComponent;
            var textRect = text.rectTransform;
            var targetY = ContentYOfViewLine(caretLine); // 生成器行定位（折行安全）
            var rowH = GetLineHeight();
            var viewH = GetViewHeight();
            var contentH = Mathf.Max(viewH, textRect.rect.height);
            var max = Mathf.Max(0f, contentH - viewH);
            // 内容锚定在矩形顶（contentY=scroll），目标行画在 scroll + targetY
            var scroll = Mathf.Clamp(targetY - (viewH - rowH) * 0.5f, 0f, max);
            textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, scroll);
            SyncTextScrollbar();
        }

        /// <summary>字符索引所在的行（生成器折行行，按行 startCharIdx 定位）。</summary>
        private static int RowOfChar(TextGenerator gen, int charIdx)
        {
            if (gen.lineCount <= 0) return -1;
            if (charIdx < gen.lines[0].startCharIdx) return 0;
            for (var r = 0; r < gen.lineCount; r++)
            {
                var end = r + 1 < gen.lineCount ? gen.lines[r + 1].startCharIdx : gen.characterCount;
                if (charIdx >= gen.lines[r].startCharIdx && charIdx < end) return r;
            }
            return gen.lineCount - 1;
        }

        /// <summary>块视图行号 → 该行首字符在内容坐标中的 Y（生成器行定位，折行安全；失败回退行高估算）。</summary>
        private float ContentYOfViewLine(int viewLine)
        {
            if (_textInput is null || viewLine < 0 || viewLine >= _blockLineCharStarts.Length) return 0f;
            var text = _textInput.textComponent;
            var gen = text.cachedTextGenerator;
            var startChar = _blockLineCharStarts[viewLine];
            if (gen.lineCount > 0 && startChar >= 0 && startChar < gen.characterCount)
            {
                var row = RowOfChar(gen, startChar);
                if (row >= 0)
                    return gen.lines[0].topY - gen.lines[row].topY; // 同坐标系相减，与 pivot 无关
            }
            return viewLine * GetLineHeight();
        }

        /// <summary>屏幕坐标 → 块视图行号（生成器行定位，折行安全；行首字符所在块行即目标行）。</summary>
        private int ViewLineAtScreen(Vector2 screenPos)
        {
            if (_textInput is null || _blockLineCharStarts.Length == 0) return -1;
            var text = _textInput.textComponent;
            var textRect = text.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(textRect, screenPos, null, out var local))
                return -1;
            var contentY = -local.y; // 矩形 pivot 在顶：local.y=0 即内容顶
            var gen = text.cachedTextGenerator;
            if (gen.lineCount > 0)
            {
                var row = -1;
                for (var i = 0; i < gen.lineCount; i++)
                {
                    var top = gen.lines[0].topY - gen.lines[i].topY;
                    var bottom = top + gen.lines[i].height;
                    if (contentY >= top - 2f && contentY < bottom) { row = i; break; }
                }
                if (row < 0) row = contentY < 0f ? 0 : gen.lineCount - 1;
                return LineOfCharInBlock(gen.lines[row].startCharIdx);
            }
            var lineHeight = GetLineHeight();
            var viewLine = Mathf.FloorToInt(contentY / lineHeight);
            return viewLine >= 0 && viewLine < _blockLineCharStarts.Length ? viewLine : -1;
        }

        /// <summary>屏幕坐标 → 字符索引（字符级：行定位 + 行内最近字符光标位）。</summary>
        private int CharIndexAtScreen(Vector2 screenPos)
        {
            if (_textInput is null) return -1;
            var text = _textInput.textComponent;
            var textRect = text.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(textRect, screenPos, null, out var local))
                return -1;
            var contentY = -local.y; // 矩形 pivot 在顶：local.y=0 即内容顶
            var gen = text.cachedTextGenerator;
            if (gen.lineCount <= 0) return -1;
            var row = -1;
            for (var i = 0; i < gen.lineCount; i++)
            {
                var top = gen.lines[0].topY - gen.lines[i].topY;
                var bottom = top + gen.lines[i].height;
                if (contentY >= top - 2f && contentY < bottom) { row = i; break; }
            }
            if (row < 0) row = contentY < 0f ? 0 : gen.lineCount - 1;
            var start = gen.lines[row].startCharIdx;
            var end = row + 1 < gen.lineCount ? gen.lines[row + 1].startCharIdx : gen.characterCount;
            for (var i = start; i < end && i < gen.characterCount; i++)
            {
                var ci = gen.characters[i];
                if (local.x <= ci.cursorPos.x + ci.charWidth * 0.5f)
                    return i; // 光标落在此字符前
            }
            return end; // 行尾
        }

        /// <summary>屏幕坐标 → 块视图行首字符索引（纯长按整行选择）。</summary>
        private int LineStartCharAtScreen(Vector2 screenPos)
        {
            var line = ViewLineAtScreen(screenPos);
            return line >= 0 ? _blockLineCharStarts[line] : -1;
        }

        /// <summary>屏幕坐标 → 块视图行尾字符索引（纯长按整行选择）。</summary>
        private int LineEndCharAtScreen(Vector2 screenPos)
        {
            var viewLine = ViewLineAtScreen(screenPos);
            if (viewLine < 0 || viewLine >= _blockLineCharStarts.Length) return -1;
            var endExcl = viewLine + 1 < _blockLineCharStarts.Length
                ? _blockLineCharStarts[viewLine + 1] - 1
                : _textInput is not null ? _textInput.text.Length : 0;
            return Math.Max(_blockLineCharStarts[viewLine], endExcl);
        }

        /// <summary>刷新自绘光标与选区覆盖层（未聚焦时显示；IME 会话中隐藏、由原生光标接管）。</summary>
        private void RefreshTextOverlays()
        {
            if (_textInput is null || _caretOverlay is null) return;
            var text = _textInput.textComponent;
            var gen = text.cachedTextGenerator;
            var focused = _textInput.isFocused;

            var showCustom = !focused;
            _caretOverlay.SetActive(showCustom && gen.characterCount > 0);
            foreach (var s in _selOverlays) s.SetActive(false);
            if (!showCustom || gen.lineCount <= 0) return;

            var rowH = GetLineHeight();
            var caret = Mathf.Clamp(_textInput.caretPosition, 0, gen.characterCount);
            if (gen.characterCount > 0)
            {
                var ci = gen.characters[Mathf.Min(caret, gen.characterCount - 1)];
                var x = caret >= gen.characterCount ? ci.cursorPos.x + ci.charWidth : ci.cursorPos.x;
                var crt = (RectTransform)_caretOverlay.transform;
                crt.anchoredPosition = new Vector2(x, ci.cursorPos.y);
                crt.sizeDelta = new Vector2(2f, rowH);
            }

            // 选区（逐行条带）
            var a = Mathf.Min(_textInput.selectionAnchorPosition, _textInput.selectionFocusPosition);
            var f = Mathf.Max(_textInput.selectionAnchorPosition, _textInput.selectionFocusPosition);
            if (f <= a || f <= 0) return;
            var band = 0;
            for (var r = 0; r < gen.lineCount && band < _selOverlays.Length; r++)
            {
                var ls = gen.lines[r].startCharIdx;
                var le = r + 1 < gen.lineCount ? gen.lines[r + 1].startCharIdx : gen.characterCount;
                var s = Mathf.Max(ls, a);
                var e = Mathf.Min(le, f);
                if (e <= s) continue;
                var c0 = gen.characters[s];
                var c1 = gen.characters[Mathf.Max(s, e - 1)];
                var x0 = c0.cursorPos.x;
                var x1 = c1.cursorPos.x + c1.charWidth;
                var srt = (RectTransform)_selOverlays[band].transform;
                srt.anchoredPosition = new Vector2(x0, c0.cursorPos.y);
                srt.sizeDelta = new Vector2(Mathf.Max(2f, x1 - x0), rowH);
                srt.gameObject.SetActive(true);
                if (band == 0)
                    Debug.Log($"[MB] selband a={a} f={f} s={s} e={e} pos=({x0:F0},{c0.cursorPos.y:F0}) size=({x1 - x0:F0},{rowH:F0}) rectH={_textInput.textComponent.rectTransform.rect.height:F0}");
                band++;
            }
        }

        /// <summary>拖动光标/选区接近文本框上下边缘时自动滚动。</summary>
        private void AutoScrollDuringDrag(Vector2 screenPos)
        {
            if (_textInput is null) return;
            var textRect = _textInput.textComponent.rectTransform;
            var parent = textRect.parent as RectTransform;
            if (parent is null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, null, out var local))
                return;
            var halfH = parent.rect.height * 0.5f;
            var edge = 40f;
            var speed = 640f * Time.unscaledDeltaTime;
            var viewH = GetViewHeight();
            var contentH = Mathf.Max(viewH, textRect.rect.height);
            var max = Mathf.Max(0f, contentH - viewH);
            var y = textRect.anchoredPosition.y;
            if (local.y > halfH - edge) y = Mathf.Max(0f, y - speed);        // 手指超出上边缘 → 内容下移（看更早）
            else if (local.y < -halfH + edge) y = Mathf.Min(max, y + speed); // 手指超出下边缘 → 内容上移（看更晚）
            if (Mathf.Abs(y - textRect.anchoredPosition.y) > 0.5f)
            {
                textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, y);
                SyncTextScrollbar();
                RefreshTextOverlays();
            }
        }

        /// <summary>时间轴定位（拖动期间每 0.1s + 松手最终确认）：解除编辑焦点 → 预览位置，
        /// 并把光标移动到对应代码行的精确音符字符处（已取消整行高亮条）。
        /// 播放已在按下时间轴时停止（DragStarted），这里只更新预览位置。</summary>
        private void OnTimelineSeek(double t)
        {
            _pendingStart = t;
            // 解除输入框焦点：其内部截断式滚动会与手动滚动冲突（键盘同时收起）
            if (_textInput is not null)
                _textInput.DeactivateInputField();
            var es = EventSystem.current;
            if (es is not null) es.SetSelectedGameObject(null);

            SeekCaretToTime(t);
            SetStatus($"已定位到 {t:0.0} 秒（点「播放」从此处开始）");
        }

        /// <summary>
        /// 把光标移动到时间 t 附近音符代码的精确字符位置，并滚动文本使该行可见（顶对齐）。
        /// 定位顺序：音符内容字符串 → 拍点内容字符串 → 拍点列号（依次回退）。
        /// </summary>
        private void SeekCaretToTime(double t)
        {
            if (_textInput is null) return;
            var tps = _parsedChart.NoteTimings;
            if (tps.IsEmpty || _blockLineCharStarts.Length == 0) return;
            var lo = 0;
            var hi = tps.Length - 1;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (tps[mid].Timing < t) lo = mid + 1; else hi = mid;
            }
            var idx = lo;
            if (idx > 0 && Math.Abs(tps[idx - 1].Timing - t) <= Math.Abs(tps[idx].Timing - t)) idx--;

            var tp = tps[idx];
            // RawTextPositionY = 块内 1 基行号（首行为 inote 头行余量），块视图行号 = Y - 1
            var viewLine = tp.RawTextPositionY - 1;
            if (viewLine < 0 || viewLine >= _blockLineCharStarts.Length)
            {
                Debug.Log($"[MB] seek caret skip viewLine={viewLine} count={_blockLineCharStarts.Length}");
                return;
            }
            var startChar = _blockLineCharStarts[viewLine];
            var endChar = viewLine + 1 < _blockLineCharStarts.Length
                ? _blockLineCharStarts[viewLine + 1] - 1
                : _textInput.text.Length;
            if (startChar < 0 || endChar <= startChar || endChar > _textInput.text.Length) return;

            // 精确字符：在目标行内按 音符内容 → 拍点内容 → 拍点列号 依次定位
            var lineText = _textInput.text.Substring(startChar, endChar - startChar + 1);
            var colInLine = -1;
            if (tp.Notes.Length > 0 && !string.IsNullOrEmpty(tp.Notes[0].RawContent))
            {
                var from = Math.Max(0, Math.Min(tp.RawTextPositionX, lineText.Length));
                colInLine = lineText.IndexOf(tp.Notes[0].RawContent, from, StringComparison.Ordinal);
            }
            if (colInLine < 0 && !string.IsNullOrEmpty(tp.RawContent))
                colInLine = lineText.IndexOf(tp.RawContent, StringComparison.Ordinal);
            if (colInLine < 0)
                colInLine = Math.Clamp(tp.RawTextPositionX, 0, lineText.Length);
            var caretChar = Math.Clamp(startChar + colInLine, startChar, endChar);

            // 滚动：目标行顶对齐视图顶（生成器行定位，折行安全）
            Canvas.ForceUpdateCanvases(); // 让 Text 生成器立即反映整文（行高/行宽测量）
            var text = _textInput.textComponent;
            var textRect = text.rectTransform;
            var targetY = ContentYOfViewLine(viewLine);
            var viewH = GetViewHeight();
            var contentH = Mathf.Max(_blockLineCharStarts.Length * GetLineHeight(), textRect.rect.height);
            var scroll = Mathf.Clamp(targetY, 0f, Mathf.Max(0f, contentH - viewH));
            textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, scroll);
            SyncTextScrollbar();

            // 移动光标到精确音符字符（未聚焦 → 自绘光标覆盖层显示）
            _textInput.caretPosition = caretChar;
            RefreshTextOverlays();
            Debug.Log($"[MB] seek caret t={t:F2} viewLine={viewLine} colInLine={colInLine} caret={caretChar} scroll={scroll:F0}");
        }

        /// <summary>由已解析谱面与音轨长度计算时间轴总时长。</summary>
        /// <summary>把缓存的波形重新挂到时间轴（播放/保存刷新重建时间轴后调用；SetChart 会清空波形）。</summary>
        private void ApplyCachedWaveform()
        {
            if (_timeline is not null && _lastWaveform is not null && _lastWaveform.Length > 1)
                _timeline.SetWaveform(_lastWaveform, _lastWaveDuration, _lastWaveOffset);
        }

        private void UpdateChartDuration()
        {
            var end = 0d;
            var tps = _parsedChart.NoteTimings;
            foreach (var tp in tps)
            {
                var notes = tp.Notes;
                for (var i = 0; i < notes.Length; i++)
                    end = Math.Max(end, tp.Timing + Math.Max(notes[i].HoldTime, Math.Max(notes[i].SlideTime, 0.5)));
            }
            // 仅当当前谱面目录确实有可用音频时，才以音频时长兜底；
            // 否则沿用上一次谱面的残留音频时长会把无音频谱面压到时间轴左缘
            var track = MobileTrackFile.Find(_currentFolder ?? string.Empty);
            var trackLen = track is not null && MobileTrackFile.IsBassNative(track)
                ? PlayManager.TrackLengthSeconds : 0d;
            _chartDuration = Math.Max(trackLen, end + 2.0);
            if (_chartDuration < 1d) _chartDuration = 60d;
            _timeline?.SetDuration(_chartDuration);
        }

        /// <summary>
        /// <summary>
        /// 物量总览（与渲染器 ObjectCounter.CountNoteSum / AstroDX 参考口径一致）：
        /// tap = 全部 tap + 滑条星星头（启动拍）；hold = hold + touchhold（不含 break-hold）；
        /// slide = 全部带头滑条（非 break-slide）；touch = touch（不含 break-touch）；
        /// break = 全部 break 变体之和（break 滑条星星头与本体分别计入，可与类型双计）。
        /// </summary>
        private void UpdateNoteCounts()
        {
            var tap = 0;
            var hold = 0;
            var slide = 0;
            var touch = 0;
            var brk = 0;
            var tps = _parsedChart.NoteTimings;
            foreach (var tp in tps)
            {
                var notes = tp.Notes;
                for (var i = 0; i < notes.Length; i++)
                {
                    var n = notes[i];
                    if (!n.IsBreak)
                    {
                        switch (n.Type)
                        {
                            case SimaiNoteType.Tap: tap++; break;
                            case SimaiNoteType.Hold:
                            case SimaiNoteType.TouchHold:
                                hold++;
                                break;
                            case SimaiNoteType.Slide:
                                if (!n.IsSlideNoHead) tap++; // 星星头（启动拍）计入 Tap
                                if (n.IsSlideBreak) brk++;
                                else slide++;
                                break;
                            case SimaiNoteType.Touch: touch++; break;
                        }
                    }
                    else
                    {
                        if (n.Type == SimaiNoteType.Slide)
                        {
                            if (!n.IsSlideNoHead) brk++; // break 滑条星星头计入 Break
                            if (n.IsSlideBreak) brk++;
                            else slide++;
                        }
                        else
                        {
                            brk++;
                        }
                    }
                }
            }
            var total = tap + hold + slide + touch + brk;
            var names = new[] { "Tap", "Hold", "Slide", "Touch", "Break", "合计" };
            var values = new[] { tap, hold, slide, touch, brk, total };
            for (var i = 0; i < names.Length && i < _countTexts.Length; i++)
                _countTexts[i].text = $"{names[i]} {values[i]}";
            Debug.Log($"[MB] counts tap={tap} hold={hold} slide={slide} touch={touch} break={brk} total={total}");
        }

        /// <summary>隐藏游戏自带的文字（渲染器区域内仅保留图形判定/音符/特效；含 uGUI Text、TMP 与 3D TextMesh）。</summary>
        private void HideGameTexts()
        {
            foreach (var t in FindObjectsOfType<Text>(true))
            {
                if (!t.enabled || t.text.Length == 0) continue;
                var rootName = t.transform.root.name;
                if (rootName == "MobileEditor" || rootName == "Dialog") continue;
                t.enabled = false;
            }
            foreach (var t in FindObjectsOfType<TMPro.TMP_Text>(true))
            {
                if (!t.enabled) continue;
                var rootName = t.transform.root.name;
                if (rootName == "MobileEditor" || rootName == "Dialog") continue;
                t.enabled = false;
            }
            foreach (var tm in FindObjectsOfType<TextMesh>(true))
            {
                var r = tm.GetComponent<Renderer>();
                if (r is not null) r.enabled = false;
            }
        }

        // ---- 编辑器自定义背景 ----

        private static string BackgroundDir => Path.Combine(Application.persistentDataPath, "CustomBG");

        private void ApplyEditorBackground()
        {
            if (_bgImage is null || _bgDimImage is null || _bgRect is null) return;
            var file = MobileSettings.Instance.BackgroundFile;
            var dim = Mathf.Clamp01(MobileSettings.Instance.BackgroundDim);
            if (string.IsNullOrEmpty(file))
            {
                _bgImage.sprite = null;
                _bgImage.enabled = false;
                _bgDimImage.color = new Color(0.078f, 0.086f, 0.102f, 0.94f); // 默认深色底（视觉规范底色）
                return;
            }
            try
            {
                var path = Path.Combine(BackgroundDir, file);
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[MB] bg file missing: {path}");
                    _bgImage.enabled = false;
                    _bgDimImage.color = new Color(0.078f, 0.086f, 0.102f, 0.94f);
                    return;
                }
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    Debug.LogWarning($"[MB] bg LoadImage failed: {path} ({bytes.Length} bytes)");
                    _bgImage.enabled = false;
                    return;
                }
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                _bgImage.sprite = sprite;
                _bgImage.enabled = true;
                // 等比铺满（cover：按较长边缩放、居中裁剪）
                var panel = (RectTransform)_bgImage.transform.parent;
                var pw = panel.rect.width;
                var ph = panel.rect.height;
                var scale = Mathf.Max(pw / Mathf.Max(1, tex.width), ph / Mathf.Max(1, tex.height));
                _bgRect.sizeDelta = new Vector2(tex.width * scale, tex.height * scale);
                _bgDimImage.color = new Color(0f, 0f, 0f, dim);
                Debug.Log($"[MB] bg applied {tex.width}x{tex.height} scale={scale:F2} dim={dim:F2} panel={pw:F0}x{ph:F0}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"ApplyEditorBackground failed: {ex}");
            }
        }

        private void PickBackground()
        {
            try { Directory.CreateDirectory(BackgroundDir); } catch { /* ignore */ }
            _pendingBgPick = true;
            if (!MobileAudioPicker.StartPickImage(BackgroundDir))
            {
                _pendingBgPick = false;
                SetStatus("无法打开系统图片选择器");
            }
            else
            {
                SetStatus("请选择背景图片…");
            }
        }

        private void InstallPickedBackground(string pickedPath)
        {
            try
            {
                var ext = Path.GetExtension(pickedPath).ToLowerInvariant();
                if (ext.Length == 0) ext = ".png";
                if (!string.IsNullOrEmpty(MobileSettings.Instance.BackgroundFile))
                {
                    try { File.Delete(Path.Combine(BackgroundDir, MobileSettings.Instance.BackgroundFile)); } catch { /* ignore */ }
                }
                var dest = Path.Combine(BackgroundDir, "bg" + ext);
                File.Move(pickedPath, dest);
                MobileSettings.Instance.BackgroundFile = Path.GetFileName(dest);
                MobileSettings.Instance.Save();
                ApplyEditorBackground();
                SetStatus("背景图片已更新");
            }
            catch (Exception ex)
            {
                SetStatus("背景设置失败: " + ex.Message);
            }
        }

        private void ClearBackground()
        {
            try
            {
                if (!string.IsNullOrEmpty(MobileSettings.Instance.BackgroundFile))
                    File.Delete(Path.Combine(BackgroundDir, MobileSettings.Instance.BackgroundFile));
            }
            catch { /* ignore */ }
            MobileSettings.Instance.BackgroundFile = string.Empty;
            MobileSettings.Instance.Save();
            ApplyEditorBackground();
            SetStatus("背景已恢复默认");
        }

        // ================= 谱面操作 =================

        private async UniTaskVoid OnOpenChartAsync(string dir)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var maidata = Path.Combine(dir, "maidata.txt");
                var fullText = await File.ReadAllTextAsync(maidata);
                _currentFolder = dir;
                _difficulty = PickDefaultDifficulty(fullText);
                _pendingStart = 0d;
                SetEditorBlockView(fullText, _difficulty);
                UpdateDiffLabel();
                _dialogRoot?.SetActive(false);

                var track = MobileTrackFile.Find(dir);
                if (track is null || !MobileTrackFile.IsBassNative(track))
                {
                    SetStatus("（无可用音频，只能编辑；放入 track.mp3/ogg/wav/flac/aiff 后可播放）");
                }
                await LoadChartIntoRendererAsync();
                SetStatus($"已打开: {Path.GetFileName(dir)}  （难度: {DiffNames[_difficulty]}）");
            }
            catch (Exception ex)
            {
                SetStatus("打开失败: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private async UniTaskVoid OnCreateChartAsync(string name)
        {
            if (_busy) return;
            if (string.IsNullOrWhiteSpace(name))
            {
                SetStatus("名称不能为空");
                return;
            }
            if (string.IsNullOrEmpty(_stagedAudioPath) || !File.Exists(_stagedAudioPath))
            {
                SetStatus("请先选择音频文件");
                return;
            }
            _busy = true;
            try
            {
                var dir = Path.Combine(ChartsRoot, name.Trim());
                Directory.CreateDirectory(dir);
                // 音频：复制为 track.<ext>（按选中文件扩展名）
                var ext = Path.GetExtension(_stagedAudioPath).ToLowerInvariant();
                if (ext.Length == 0) ext = ".mp3";
                var trackPath = Path.Combine(dir, "track" + ext);
                File.Copy(_stagedAudioPath, trackPath, true);
                try { File.Delete(_stagedAudioPath); } catch { /* ignore */ }
                _stagedAudioPath = null;

                _fileText =
                    "&title=" + name.Trim() + "\n" +
                    "&artist=Unknown\n" +
                    "&first=0\n" +
                    "&des_1=NEW\n" +
                    "&lv_1=1\n" +
                    "&inote_1=\n" +
                    "\n";
                await File.WriteAllTextAsync(Path.Combine(dir, "maidata.txt"), _fileText, Encoding.UTF8);
                _currentFolder = dir;
                _difficulty = 0;
                SetEditorBlockView(_fileText, 0);
                UpdateDiffLabel();
                _dialogRoot?.SetActive(false);
                SetStatus($"已新建: {name.Trim()}  （音频: track{ext}）");
            }
            catch (Exception ex)
            {
                SetStatus("新建失败: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private async UniTaskVoid OnSaveAsync()
        {
            await SaveAsync();
        }

        /// <summary>把当前文本解析后装载进渲染器（媒体沿用当前谱面目录）。</summary>
        private async UniTask LoadChartIntoRendererAsync()
        {
            try
            {
                SetStatus("正在解析谱面…");
                // 解析移出主线程：避免大谱面打开时界面假死
                var file = await UniTask.RunOnThreadPool(() => SimaiParser.ParseAsync(_fileText, "mobile").GetAwaiter().GetResult());
                _parsedFile = file;
                _chartOffset = file.Offset;
                var chart = file.Charts[Mathf.Clamp(_difficulty, 0, 7)];
                if (chart.IsEmpty)
                {
                    SetStatus("该难度为空，请选择其它难度");
                    _parsedChart = chart;
                    _timeline?.SetChart(chart);
                    UpdateChartDuration();
                    UpdateNoteCounts();
                    return;
                }
                _parsedChart = chart;
                _timeline?.SetChart(chart);
                UpdateChartDuration();
                UpdateNoteCounts();

                SetStatus("正在装载谱面…");
                if (!string.IsNullOrEmpty(_currentFolder))
                {
                    var track = MobileTrackFile.Find(_currentFolder);
                    if (track is not null && MobileTrackFile.IsBassNative(track))
                    {
                        var bg = Path.Combine(_currentFolder, "bg.jpg");
                        if (!File.Exists(bg)) bg = Path.Combine(_currentFolder, "bg.png");
                        var pv = Path.Combine(_currentFolder, "pv.mp4");
                        if (!File.Exists(pv)) pv = Path.Combine(_currentFolder, "bg.mp4");
                        await _playManager.LoadAsync(track, File.Exists(bg) ? bg : string.Empty, File.Exists(pv) ? pv : null);
                        UpdateChartDuration(); // 音频装载后重算：以当前曲目实际时长兜底

                        // 波形底纹（同音频直接复用缓存；换曲才重新解码降采样）
                        if (_lastWaveTrack == track && _lastWaveform is not null && _lastWaveform.Length > 1)
                        {
                            _timeline?.SetWaveform(_lastWaveform, _lastWaveDuration, _lastWaveOffset);
                        }
                        else
                        {
                            try
                            {
                                var trackCapture = track;
                                var offsetCapture = _chartOffset;
                                var wave = await UniTask.RunOnThreadPool(() => _audioManager?.DecodeWaveform(trackCapture, 1000));
                                if (wave is not null && wave.Length > 1)
                                {
                                    _lastWaveform = wave;
                                    _lastWaveDuration = PlayManager.TrackLengthSeconds;
                                    _lastWaveOffset = offsetCapture;
                                    _lastWaveTrack = track;
                                    _timeline?.SetWaveform(wave, _lastWaveDuration, _lastWaveOffset);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"waveform decode failed: {ex.Message}");
                            }
                        }
                    }
                }
                await _playManager.LoadChartAsync(file, chart, _difficulty);
            }
            catch (Exception ex)
            {
                SetStatus("解析失败: " + ex.Message);
            }
        }

        private async UniTaskVoid OnPlayAsync(double startAt = -1d)
        {
            if (_busy || _playing) return;
            if (startAt < 0d) startAt = _pendingStart;
            _busy = true;
            try
            {
                CommitEditorBlock();
                SetStatus("正在解析谱面…");
                var file = await UniTask.RunOnThreadPool(() => SimaiParser.ParseAsync(_fileText, "mobile").GetAwaiter().GetResult());
                var chart = file.Charts[Mathf.Clamp(_difficulty, 0, 7)];
                if (chart.IsEmpty)
                {
                    SetStatus("该难度为空，无法播放");
                    return;
                }
                if (string.IsNullOrEmpty(_currentFolder))
                {
                    SetStatus("请先打开谱面（需要目录中的音频文件）");
                    return;
                }
                _parsedChart = chart;
                _timeline?.SetChart(chart);
                ApplyCachedWaveform(); // 播放路径重建时间轴后恢复波形（同音频复用缓存）
                UpdateChartDuration();
                UpdateNoteCounts();
                SetStatus("正在装载谱面…");
                await _playManager.LoadLocalChartDataAsync(_currentFolder, file, chart, _difficulty);
                await _playManager.PlayAsync(PlaybackMode.Normal, startAt + _chartOffset, _speed, string.Empty);
                _playing = true;
                if (_playStopButton is not null)
                {
                    _playStopButton.GetComponentInChildren<Text>().text = "停止";
                }
                SetStatus(startAt > 0.05d
                    ? $"从 {startAt:0.0}s 开始播放…（编辑仍可用，保存会自动刷新）"
                    : "播放中…（编辑仍可用，保存会自动刷新）");
            }
            catch (Exception ex)
            {
                SetStatus("播放失败: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private void OnStop()
        {
            _ = OnStopAsync();
        }

        private async UniTaskVoid OnStopAsync()
        {
            try { await _playManager.StopAsync(); }
            catch (Exception ex) { Debug.LogWarning($"MobileBootstrap: stop failed: {ex}"); }
            _playing = false;
            if (_playStopButton is not null)
            {
                _playStopButton.GetComponentInChildren<Text>().text = "播放";
            }
            SetStatus("已停止");
        }

        // ================= 元数据 =================

        private void UpdateDiffLabel()
        {
            if (_diffLabel is not null)
                _diffLabel.text = $"难度: {DiffNames[_difficulty]}  Lv.{GetMetaValue("lv")}";
        }

        private int PickDefaultDifficulty(string text)
        {
            // 选择第一个非空难度；否则 0（仅做轻量文本探测，不完整解析，避免主线程阻塞）
            for (var i = 1; i <= 8; i++)
            {
                var prefix = "&inote_" + i + "=";
                foreach (var line in text.Split('\n'))
                    if (line.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
                        return i - 1;
            }
            return 0;
        }

        private string GetMetaValue(string key)
        {
            if (string.IsNullOrEmpty(_fileText)) return string.Empty;
            var suffix = key == "first" ? string.Empty : "_" + (_difficulty + 1);
            var prefix = "&" + key + suffix + "=";
            foreach (var line in _fileText.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith(prefix, StringComparison.Ordinal))
                    return t.Substring(prefix.Length).Trim();
            }
            return string.Empty;
        }

        /// <summary>元数据字段实时写入完整谱面文本头部（&lv_N / &des_N / &first），不触碰当前块视图；「保存」时随文件落盘。</summary>
        private void WriteMetaToFile(string key, string value)
        {
            _fileText = ReplaceMeta(_fileText, key, value);
            UpdateDiffLabel();
        }

        // ================= 导出 zip =================

        /// <summary>未保存修改检测：当前块视图与完整文件中的块内容是否一致。</summary>
        private bool HasUnsavedChanges()
        {
            if (_textInput is null || string.IsNullOrEmpty(_fileText)) return false;
            var edited = _textInput.text.Replace("\r\n", "\n").TrimEnd('\n');
            var (start, endExcl, remainder) = ScanInoteBlock(_fileText, _difficulty);
            var allLines = _fileText.Split('\n');
            var sb = new StringBuilder();
            if (remainder.Length > 0) sb.Append(remainder).Append('\n');
            for (var i = start + 1; i < endExcl; i++) sb.Append(allLines[i]).Append('\n');
            var committed = sb.ToString().TrimEnd('\n');
            return !string.Equals(committed, edited, StringComparison.Ordinal);
        }

        private void ShowExportConfirmOrStart()
        {
            if (string.IsNullOrEmpty(_currentFolder))
            {
                SetStatus("请先打开谱面");
                return;
            }
            if (HasUnsavedChanges())
                ShowExportSavePrompt();
            else
                BuildAndPickExportZip();
        }

        private void ShowExportSavePrompt()
        {
            if (_dialogRoot is null || _dialogPanel is null) return;
            _dialogRoot.SetActive(true);
            ClearChildren(_dialogPanel.transform);
            MakeText(_dialogPanel.transform, "编辑器内容尚未保存", 34, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -140), new Vector2(900, 70));
            MakeText(_dialogPanel.transform, "导出将使用已保存的版本。是否先保存再导出？", 26, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -240), new Vector2(900, 60));
            var saveBtn = MakeButton(_dialogPanel.transform, "保存并导出", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-160, 60), new Vector2(300, 84));
            saveBtn.onClick.AddListener(() => _ = ExportAfterSaveAsync());
            var cancelBtn = MakeButton(_dialogPanel.transform, "取消", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(160, 60), new Vector2(240, 84));
            cancelBtn.onClick.AddListener(ShowFileMenu);
        }

        private async UniTaskVoid ExportAfterSaveAsync()
        {
            _dialogRoot?.SetActive(false);
            if (await SaveAsync()) BuildAndPickExportZip();
        }

        /// <summary>收集谱面目录中存在的文件（maidata.txt + 音频 + 图片/pv），平铺打包为 zip 后发起系统目录选择。</summary>
        private void BuildAndPickExportZip()
        {
            try
            {
                var dir = _currentFolder;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    SetStatus("谱面目录不存在");
                    return;
                }
                var files = new List<string>();
                var maidata = Path.Combine(dir, "maidata.txt");
                if (File.Exists(maidata)) files.Add(maidata);
                var track = MobileTrackFile.Find(dir);
                if (track is not null && File.Exists(track)) files.Add(track);
                foreach (var extra in new[] { "bg.jpg", "bg.png", "pv.mp4", "bg.mp4" })
                {
                    var p = Path.Combine(dir, extra);
                    if (File.Exists(p)) files.Add(p);
                }
                if (files.Count == 0)
                {
                    SetStatus("谱面目录为空，无可导出文件");
                    return;
                }

                var staging = Path.Combine(Application.temporaryCachePath, "export_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                foreach (var f in files)
                    File.Copy(f, Path.Combine(staging, Path.GetFileName(f)), true);

                var zipName = SanitizeFileName(Path.GetFileName(dir)) + ".zip";
                var zipPath = Path.Combine(Application.temporaryCachePath, zipName);
                if (File.Exists(zipPath)) File.Delete(zipPath);
                System.IO.Compression.ZipFile.CreateFromDirectory(staging, zipPath);
                try { Directory.Delete(staging, true); } catch { /* ignore */ }

                _pendingExportZip = zipPath;
                _pendingExportName = zipName;
                if (!MobileChartPicker.StartExportPick(zipPath))
                {
                    SetStatus("无法打开系统目录选择器");
                    try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* ignore */ }
                }
                else
                {
                    SetStatus("请选择导出目标文件夹…");
                }
            }
            catch (Exception ex)
            {
                SetStatus("导出失败: " + ex.Message);
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "chart";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            var s = new string(chars).Trim();
            return string.IsNullOrEmpty(s) ? "chart" : s;
        }

        /// <summary>保存（返回是否成功；供导出流程“保存并导出”复用）。</summary>
        private async UniTask<bool> SaveAsync()
        {
            if (_busy) return false;
            if (string.IsNullOrEmpty(_currentFolder))
            {
                SetStatus("尚未打开谱面");
                return false;
            }
            _busy = true;
            try
            {
                CommitEditorBlock();
                await File.WriteAllTextAsync(Path.Combine(_currentFolder, "maidata.txt"), _fileText, Encoding.UTF8);
                SetEditorBlockView(_fileText, _difficulty);
                await LoadChartIntoRendererAsync();
                SetStatus("已保存");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("保存失败: " + ex.Message);
                return false;
            }
            finally
            {
                _busy = false;
            }
        }

        private string ReplaceMeta(string text, string key, string value)
        {
            var suffix = key == "first" ? string.Empty : "_" + (_difficulty + 1);
            var prefix = "&" + key + suffix + "=";
            var sb = new StringBuilder();
            var found = false;
            foreach (var line in text.Split('\n'))
            {
                var t = line.TrimEnd('\r');
                if (!found && t.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
                {
                    sb.Append(prefix).Append(value).Append('\n');
                    found = true;
                }
                else sb.Append(line).Append('\n');
            }
            if (!found)
                sb.Append(prefix).Append(value).Append('\n');
            return sb.ToString().TrimEnd('\n') + "\n";
        }

        /// <summary>轻量读取谱面标题与作者（不完整解析）。</summary>
        private static (string title, string designer) ReadMeta(string maidataPath)
        {
            string title = string.Empty, designer = string.Empty;
            try
            {
                using var reader = new StreamReader(maidataPath);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    line = line.TrimStart('\uFEFF', ' ', '\t'); // 兼容带 BOM 的 maidata
                    if (line.StartsWith("&title=")) title = line.Substring("&title=".Length).Trim();
                    else if (line.StartsWith("&des=")) designer = line.Substring("&des=".Length).Trim();
                    if (title.Length > 0 && designer.Length > 0) break;
                }
            }
            catch { /* ignore */ }
            if (title.Length == 0) title = Path.GetFileName(Path.GetDirectoryName(maidataPath)) ?? "未命名";
            return (title, designer);
        }
    }
}
