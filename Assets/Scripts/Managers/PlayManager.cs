#nullable enable

using Cysharp.Threading.Tasks;
using MajdataViewX.Base;
using MajdataViewX.Notes;
using MajdataViewX.Notes.SlideUtils;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.MajSetting;
using MajdataViewX.Types.MajWs;
using MajSimai;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading;
using Unity.Properties;
using UnityEngine;
using UnityEngine.SocialPlatforms;
using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class PlayManager : MonoBehaviour
    {
        public static ViewSummary Summary => new()
        {
            State = _state,
            ErrMsg = _errMsg,
        };

        /// <summary>当前播放时间（秒）。移动端编辑器时间轴用。</summary>
        public static double CurrentAudioTime => _timeProvider?.AudioTime ?? 0d;

        /// <summary>当前谱面时间（音频时间 − first 偏移；与渲染器画面同步的时间域）。</summary>
        public static double CurrentNoteTime => _timeProvider?.NoteTime ?? 0d;

        /// <summary>当前曲目时长（秒；未加载时为 0）。移动端编辑器时间轴用。</summary>
        public static double TrackLengthSeconds => _audioManager?.TrackLengthSeconds ?? 0d;

        // 直接存储 MajSimai 原始类型：Update 从共享内存拿到已解析数据，Play 不再全量解析
        private static SimaiFile _file = SimaiFile.Empty(string.Empty, string.Empty);
        private static SimaiChart _chart = SimaiChart.Empty;
        private MemoryMappedFile mmfChartData;
        private MemoryMappedViewAccessor mmvChartData;

        private static ViewStatus _state = ViewStatus.Idle;
        private static string _errMsg = string.Empty;

        private static Thread? _audioManagerThread;
        private static int _audioManagerThreadRunning;

        private static MajViewSetting _setting = new();

        private SpriteRenderer bgCover;
        private SpriteRenderer bgOutsideCover;
        private GameObject canvasButtons;

        private void Awake()
        {
            _playManager = this;
        }

        // 这里是游戏内部的东西的启动初始化
        private void Start()
        {
            // 输入/触摸数据结构必须最先就绪：任何后续初始化失败都不能导致判定结构未分配
            // （否则首帧 OnLateUpdate 会在未分配的 NativeArray 上崩溃）。
            MajBurst.__DataSS.Data = new MajBurstData
            {
                TimeData = new(),
                InputData = new(),
                MultTouchHandler = new(),
                GlobalRandom = new((uint)"MajdataX".GetHashCode()),
            };
            //MajBurst.TimeData.Init();
            MajBurst.InputData.Init();
            MajBurst.MultTouchHandler.Init();

            _ = new InputManager();

            bgCover = GameObject.Find("BgCover").GetComponent<SpriteRenderer>();
            bgOutsideCover = GameObject.Find("BgOutsideCover").GetComponent<SpriteRenderer>();
            canvasButtons = GameObject.Find("CanvasButtons");

            try
            {
                _ = new AudioManager();
                Volatile.Write(ref _audioManagerThreadRunning, 1);
                _audioManagerThread = new Thread(() =>
                {
                    while (Volatile.Read(ref _audioManagerThreadRunning) != 0)
                    {
                        _audioManager.OnUpdate();
                        Thread.Sleep(1);
                    }
                })
                {
                    IsBackground = true,
                    Name = "Majdata SFX Trigger",
                    Priority = System.Threading.ThreadPriority.AboveNormal,
                };
                _audioManagerThread.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError("AudioManager init failed: " + ex);
            }

            SlideTableNeo.InitializeStandardSlideTable();

            var mmfChartDataFileStream = new FileStream(
                    MajEnv.MmfChartDataPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite
                );
            if (mmfChartDataFileStream.Length < MajEnv.MmfChartDataCapacity)
                mmfChartDataFileStream.SetLength(MajEnv.MmfChartDataCapacity);
            mmfChartData = MemoryMappedFile.CreateFromFile(
                mmfChartDataFileStream,
                null,
                MajEnv.MmfChartDataCapacity,
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                false
            );
            mmvChartData = mmfChartData.CreateViewAccessor();
        }

        private bool CheckIsLoaded() => _audioManager.IsTrackLoaded &&
                                        _bgManager.IsBgLoaded &&
                                        _bgManager.IsVideoLoaded;

        public async UniTask LoadAsync(string audioPath, string bgPath, string? pvPath)
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();
            _state = ViewStatus.Busy;

            try
            {
                await UniTask.SwitchToMainThread();

                //audio
                _audioManager.LoadTrack(audioPath);

                //bg
                if (File.Exists(bgPath))
                {
                    BgManager.hasBg = true;
                    _bgManager.LoadBG(bgPath);
                }
                else
                {
                    BgManager.hasBg = false;
                }

                //video
                if (pvPath is not null && File.Exists(pvPath))
                {
                    BgManager.hasVideo = true;
                    _bgManager.LoadVideo(pvPath);
                }
                else
                {
                    BgManager.hasVideo = false;
                }

                _state = ViewStatus.Loaded;
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
            }
        }

        public void Setting(MajViewSetting setting, MajVolumeSetting volumeSetting)
        {
            _setting = setting;

            NoteHelper.NoteSettingsSS.Data = new NoteSettings()
            {
                AutoPlayMode = _setting.AutoMode,
                TapSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(_setting.TapSpeed + 0.9975f, -0.985558604f))),
                TouchSpeed = _setting.TouchSpeed,
                LegacySlideLayer = _setting.LegacySlideLayer,
                SmoothSlideAnime = _setting.SmoothSlideAnime,
                MineAutoSlide = _setting.MineAutoSlide,
            };
            //audio
            _audioManager.Setting(setting.GlobalAudioOffset, volumeSetting);
            //simulate
            _inputManager.ShowHand = _setting.ShowHand;
            //counter
            _objectCounter.Setting(_setting.ComboStatusType, _setting.UIType);
            //bg
            bgCover.color = new Color(0f, 0f, 0f, _setting.BackgroundDim);
            bgOutsideCover.color = new Color(0f, 0f, 0f, _setting.BackgroundOutsideDim);
            _bgManager.ResizeBg = _setting.ResizeBg;
        }

        public async UniTask UpdateAsync(long fileLength, long chartLength, int selectedDiff)
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            var previousState = _state;
            _state = ViewStatus.Busy;

            try
            {
                // 从共享内存读取 Edit 写入的两段 MemoryPack 字节并反序列化：
                // [0..fileLength) = SimaiFile 元数据（Charts 已 Ignore），[fileLength..) = SimaiChart 时序
                var fileBuffer = new byte[fileLength];
                mmvChartData.ReadArray(0, fileBuffer, 0, (int)fileLength);
                var chartBuffer = new byte[chartLength];
                mmvChartData.ReadArray(fileLength, chartBuffer, 0, (int)chartLength);

                var file = MemoryPackSerializer.Deserialize<SimaiFile>(fileBuffer) ?? SimaiFile.Empty(string.Empty, string.Empty);
                var chart = MemoryPackSerializer.Deserialize<SimaiChart>(chartBuffer) ?? SimaiChart.Empty;

                await LoadChartAsync(file, chart, selectedDiff);
            }
            finally
            {
                _state = previousState;
            }
        }

        /// <summary>
        /// 装载谱面数据（与来源无关）：更新计时 offset、生成 Answer 音效、计数器统计并加载音符。
        /// </summary>
        public async UniTask LoadChartAsync(SimaiFile file, SimaiChart chart, int selectedDiff)
        {
            _file = file;
            _chart = chart;

            _timeProvider.offset = _file.Offset;
            //answer
            var clockCount = 0;
            var clockCommand = file.Commands.FirstOrDefault(c => c.Prefix == "clock_count");
            if (clockCommand != null) int.TryParse(clockCommand.Value, out clockCount);
            _audioManager.GenerateAnswerSFX(_chart, clockCount);

            //counter
            _objectCounter.ResetLoaded();
            _objectCounter.CountNoteSum(_chart);
            _objectCounter.ReportMeterBpm(_chart);

            await _dataLoader.Load(_chart, file.Title, file.Artist, selectedDiff);
        }

        /// <summary>
        /// 移动端独立装载：本地解析 maidata.txt + 查找 track/bg/pv（不依赖编辑器 WS/共享内存）。
        /// 注意：不自行占用 Busy 状态，由内部 LoadAsync 管理，否则与 LoadAsync 的 Busy 等待互相死锁。
        /// </summary>
        public async UniTask LoadLocalChartAsync(string chartDir, int selectedDiff)
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            try
            {
                await UniTask.SwitchToMainThread();

                var maidata = Path.Combine(chartDir, "maidata.txt");
                if (!File.Exists(maidata))
                    throw new FileNotFoundException($"maidata.txt 不存在: {chartDir}");

                await using var stream = new FileStream(maidata, FileMode.Open, FileAccess.Read);
                var file = await SimaiParser.ParseAsync(stream);
                var chart = file.Charts[Mathf.Clamp(selectedDiff, 0, 7)];
                if (chart.IsEmpty)
                    throw new InvalidOperationException("该难度为空");

                await LoadLocalChartDataAsync(chartDir, file, chart, selectedDiff);
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
                throw;
            }
        }

        /// <summary>
        /// 移动端独立装载（已解析数据版）：从谱面目录查找媒体并装载，再装载谱面数据。
        /// 注意：不自行占用 Busy 状态，由内部 LoadAsync 管理，否则与 LoadAsync 的 Busy 等待互相死锁。
        /// </summary>
        public async UniTask LoadLocalChartDataAsync(string chartDir, SimaiFile file, SimaiChart chart, int selectedDiff)
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            try
            {
                await UniTask.SwitchToMainThread();

                var track = MajdataViewX.Mobile.MobileTrackFile.Find(chartDir);
                if (track is null)
                    throw new FileNotFoundException($"未找到 track 音频（支持 {string.Join(", ", MajdataViewX.Mobile.MobileTrackFile.SupportedExtensions)}）: {chartDir}");
                if (!MajdataViewX.Mobile.MobileTrackFile.IsBassNative(track))
                    throw new InvalidOperationException(
                        $"暂不支持的音频格式 {Path.GetExtension(track)}，请使用 mp3/ogg/wav/flac/aiff（移动端无 ffmpeg 转码）");

                var bg = Path.Combine(chartDir, "bg.jpg");
                if (!File.Exists(bg)) bg = Path.Combine(chartDir, "bg.png");
                var pv = Path.Combine(chartDir, "pv.mp4");
                if (!File.Exists(pv)) pv = Path.Combine(chartDir, "bg.mp4");

                await LoadAsync(track, File.Exists(bg) ? bg : string.Empty, File.Exists(pv) ? pv : null);

                await LoadChartAsync(file, chart, selectedDiff);
                _state = ViewStatus.Loaded;
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
                throw;
            }
        }

        public async UniTask PlayAsync(PlaybackMode playmode, double startAt, float speed, string recordPath)
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            if (_state is not (ViewStatus.Loaded or ViewStatus.Paused))
                return;

            _state = ViewStatus.Busy;
            try
            {
                await UniTask.SwitchToMainThread();
                var ignoreOffset = startAt - _file.Offset;

                //bg
                _bgManager.ShowBG();
                _bgManager.ShowVideo();
                //sfx
                _audioManager.ResetAnswerSFX(ignoreOffset);
                //counter
                _objectCounter.ResetCur();
                _objectCounter.CountIgnoreNoteCountAsync(_chart, ignoreOffset);
                //notes
                //MajBurst.InputData.ResetState(); in ResetLoadedNote
                _noteManager.ResetState(); //reset djauto hands (PlayUpdateJob is still running when IsStart==false)
                _noteManager.ResetLoadedNote(ignoreOffset);
                _noteManager.ResetLoadedPlay(ignoreOffset);
                MajBurst.MultTouchHandler.ResetMultTouchState();

                switch (playmode)
                {
                    case PlaybackMode.Normal:
                        _allPerfectManager.enabled = false;

                        _timeProvider.SetStartTime(startAt, _file.Offset, speed, playmode);
                        _audioManager.PlayTrack();
                        break;
                    case PlaybackMode.IncludeOp:
                        _allPerfectManager.enabled = true;

                        _bgManager.PlaySongDetail();
                        _audioManager.noteSfxPlaybackRequests[AudioManager.TRACK_START] = true; //track_start

                        _timeProvider.SetStartTime(startAt, _file.Offset, speed, playmode);
                        _audioManager.PlayTrack();
                        break;
                    case PlaybackMode.Record:
                        if (!Directory.Exists(recordPath))
                        {
                            throw new InvalidPathException($"maidata path is required");
                        }

                        canvasButtons.SetActive(false);
                        _allPerfectManager.enabled = true;

                        _bgManager.PlaySongDetail();
                        _screenRecorder.StartRecording(recordPath,
                            _setting.OutputFps, _setting.ExportQuality,
                            () =>
                            {
                                _timeProvider.SetStartTime(startAt, _file.Offset, speed, playmode, _setting.OutputFps);
                            }).ContinueWith(() =>
                        {
                            canvasButtons.SetActive(true);
                            _state = ViewStatus.Loaded;
                        }).Forget();
                        break;
                    case PlaybackMode.Preview:
                        _allPerfectManager.enabled = false;
                        _state = ViewStatus.Paused;
                        return;
                }

                _state = ViewStatus.Playing;
                return;
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
                return;
            }
        }

        public async UniTask PauseAsync()
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            if (_state is not ViewStatus.Playing)
                return;

            _state = ViewStatus.Busy;
            try
            {
                await UniTask.SwitchToMainThread();

                _timeProvider.Pause();

                _bgManager.PauseVideo();

                _audioManager.PauseTrack();
                _audioManager.PauseTouchHoldSound();

                _state = ViewStatus.Paused;
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
            }
        }

        public async UniTask StopAsync()
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            if (_state is not (ViewStatus.Playing or ViewStatus.Paused or ViewStatus.Error))
                return;

            _state = ViewStatus.Busy;
            try
            {
                await UniTask.SwitchToMainThread();

                _screenRecorder.StopRecording();
                await UniTask.Yield();

                //_objectCounter.ResetCur();
                _timeProvider.ResetState();
                _audioManager.ResetState();
                _bgManager.ResetState();
                _effectManager.ResetState();
                _allPerfectManager.ResetState();

                _state = CheckIsLoaded() ? ViewStatus.Loaded : ViewStatus.Idle;
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
            }
        }

        private void OnDestroy()
        {
            Volatile.Write(ref _audioManagerThreadRunning, 0);
            if (_audioManagerThread is { IsAlive: true } &&
                _audioManagerThread != Thread.CurrentThread)
            {
                _audioManagerThread.Join();
            }
            _audioManagerThread = null;

            _audioManager.OnDestroy();
            _inputManager.OnDestroy();
            MajBurst.InputData.Dispose();

            mmvChartData?.Dispose();
            mmfChartData?.Dispose();
        }
    }
}