using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/**
 * 游戏音频模块。
 * 包含音效类型定义（GameSfx）和全局音频服务（GameAudioService），
 * 负责 BGM 与音效的加载、播放、音量控制及状态持久化。
 */
    /// <summary>
    /// 游戏音效枚举。
    /// </summary>
    public enum GameSfx
    {
        /// <summary>
        /// 强化/升级音效。
        /// </summary>
        Intensify,

        /// <summary>
        /// 发送/发射音效。
        /// </summary>
        Send,

        /// <summary>
        /// 发牌和单击出牌音效。
        /// </summary>
        Card,

        /// <summary>
        /// 护盾破碎音效。
        /// </summary>
        ShieldBreak,

        /// <summary>
        /// 战斗阶段切换音效。
        /// </summary>
        Next,

        /// <summary>
        /// 战斗结束音效。
        /// </summary>
        Pass
    }

    /// <summary>
    /// 游戏音频服务。
    /// 负责 BGM 的播放/暂停、音效的播放、音量持久化等。
    /// 全局唯一单例，通过 Ensure() 创建，DontDestroyOnLoad。
    /// </summary>
    public sealed class GameAudioService : MonoBehaviour
    {
        #region 常量

        // PlayerPrefs 中保存音量的键名
        private const string VolumeKey = "NCG.Audio.Volume";
        private const string BgmVolumeKey = "NCG.Audio.BgmVolume";
        private const string SfxVolumeKey = "NCG.Audio.SfxVolume";

        // PlayerPrefs 中保存 BGM 启用状态的键名
        private const string BgmKey = "NCG.Audio.BgmEnabled";

        #endregion

        #region 字段

        // 全局单例实例
        private static GameAudioService instance;

        // 预加载的音效剪辑数组，索引对应 GameSfx 枚举值
        private readonly AudioClip[] sfx = new AudioClip[6];

        // BGM 播放用的 AudioSource
        private AudioSource bgmSource;

        // 音效播放用的 AudioSource
        private AudioSource sfxSource;

        // BGM 音频剪辑
        private AudioClip bgmClip;

        // 标记音效剪辑是否已加载完成
        private bool loaded;

        // 防止多个场景同时触发重复加载；资源服务尚未注册时允许后续 Ensure 重试。
        private bool loading;

        #endregion

        #region 属性

        /// <summary>
        /// 全局单例实例。
        /// </summary>
        public static GameAudioService Instance => instance;

        /// <summary>
        /// 当前音量值（0 ~ 1）。
        /// </summary>
        public float Volume { get; private set; }

        public float BgmVolume { get; private set; }

        public float SfxVolume { get; private set; }

        /// <summary>
        /// BGM 是否已启用。
        /// </summary>
        public bool BgmEnabled { get; private set; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 获取或创建 GameAudioService 单例。
        /// 首次调用时创建新 GameObject 并挂载组件，标记为场景切换不销毁。
        /// </summary>
        public static GameAudioService Ensure()
        {
            // 已有实例直接返回
            if (instance != null)
            {
                instance.BeginLoadIfNeeded();
                return instance;
            }

            var root = new GameObject("GameAudioService");
            instance = root.AddComponent<GameAudioService>();
            DontDestroyOnLoad(root);
            return instance;
        }

        private void Awake()
        {
            // 如果已有实例且不是自己，销毁多余副本
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            // 从 PlayerPrefs 读取持久化的音量与 BGM 开关，默认音量 1、BGM 开启
            Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            BgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmVolumeKey, Volume));
            SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, Volume));
            BgmEnabled = PlayerPrefs.GetInt(BgmKey, 1) != 0;

            // 创建两个 AudioSource：BGM（循环播放）和 SFX（一次性播放）
            bgmSource = gameObject.AddComponent<AudioSource>();
            sfxSource = gameObject.AddComponent<AudioSource>();
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            bgmSource.spatialBlend = 0f;
            bgmSource.mute = false;
            bgmSource.ignoreListenerPause = true;
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
            sfxSource.mute = false;
            sfxSource.ignoreListenerPause = true;
            EnsureAudioListener();

            BeginLoadIfNeeded();
        }

        private void BeginLoadIfNeeded()
        {
            if (loaded || loading)
            {
                return;
            }

            loading = true;
            _ = LoadClipsAsync();
        }

        #endregion

        #region 音频设置

        /// <summary>
        /// 设置音量并持久化。
        /// </summary>
        /// <param name="value">音量值，会被 Clamp 到 0 ~ 1 范围。</param>
        public void SetVolume(float value)
        {
            Volume = Mathf.Clamp01(value);
            BgmVolume = Volume;
            SfxVolume = Volume;
            PlayerPrefs.SetFloat(VolumeKey, Volume);
            PlayerPrefs.SetFloat(BgmVolumeKey, BgmVolume);
            PlayerPrefs.SetFloat(SfxVolumeKey, SfxVolume);
            PlayerPrefs.Save();

            // 实时应用到当前 BGM 音量
            ApplyVolume();
        }

        public void SetBgmVolume(float value)
        {
            BgmVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(BgmVolumeKey, BgmVolume);
            PlayerPrefs.Save();
            ApplyVolume();
        }

        public void SetSfxVolume(float value)
        {
            SfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(SfxVolumeKey, SfxVolume);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 设置 BGM 启用状态并持久化。
        /// 开启时立即播放 BGM；关闭时停止 BGM。
        /// </summary>
        /// <param name="enabled">是否启用 BGM。</param>
        public void SetBgmEnabled(bool enabled)
        {
            BgmEnabled = enabled;
            PlayerPrefs.SetInt(BgmKey, enabled ? 1 : 0);
            PlayerPrefs.Save();

            if (enabled)
                PlayBgm();
            else if (bgmSource != null)
                bgmSource.Stop();
        }

        /// <summary>
        /// 切换 BGM 的启用/禁用状态。
        /// </summary>
        public void ToggleBgm()
        {
            SetBgmEnabled(!BgmEnabled);
        }

        #endregion

        #region 播放音频

        /// <summary>
        /// 播放 BGM。
        /// 仅在 BgmEnabled、bgmSource 和 bgmClip 均有效时才会播放。
        /// </summary>
        public void PlayBgm()
        {
            // 条件不满足时跳过播放
            if (!BgmEnabled || bgmSource == null || bgmClip == null)
            {
                Debug.LogWarning($"[Audio] BGM未播放: enabled={BgmEnabled}, source={bgmSource != null}, clip={bgmClip != null}, volume={BgmVolume}");
                return;
            }

            // 如果 AudioSource 当前未绑定 bgmClip，则绑定
            if (bgmSource.clip != bgmClip)
                bgmSource.clip = bgmClip;

            // 尚未播放时启动播放
            if (!bgmSource.isPlaying)
                bgmSource.Play();

            ApplyVolume();
            Debug.Log($"[Audio] BGM开始播放: clip={bgmClip.name}, sourceVolume={bgmSource.volume}, isPlaying={bgmSource.isPlaying}, listenerPause={AudioListener.pause}, listenerVolume={AudioListener.volume}");
        }

        /// <summary>
        /// 播放指定的音效（一次性 PlayOneShot）。
        /// 音效未加载完成或音量为 0 时不会播放。
        /// </summary>
        /// <param name="effect">要播放的音效类型。</param>
        public void PlaySfx(GameSfx effect)
        {
            // 条件不满足时跳过播放
            if (sfxSource == null || !loaded || SfxVolume <= 0f)
                return;

            var clip = sfx[(int)effect];
            if (clip != null)
                sfxSource.PlayOneShot(clip, SfxVolume);
        }

        #endregion
        

        /// <summary>
        /// 将当前音量同步应用到 bgmSource。
        /// BGM 禁用时音量强制为 0。
        /// </summary>
        private void ApplyVolume()
        {
            if (bgmSource != null)
                bgmSource.volume = BgmEnabled ? BgmVolume : 0f;
        }

        private void EnsureAudioListener()
        {
            var listeners = FindObjectsOfType<AudioListener>();
            for (var i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && listeners[i].isActiveAndEnabled)
                {
                    AudioListener.pause = false;
                    AudioListener.volume = 1f;
                    return;
                }
            }

            var listener = gameObject.AddComponent<AudioListener>();
            listener.enabled = true;
            AudioListener.pause = false;
            AudioListener.volume = 1f;
            Debug.LogWarning("[Audio] 当前场景没有启用的 AudioListener，已在 GameAudioService 上创建运行时 Listener");
        }

        /// <summary>
        /// 通过 IGameAssetService 异步加载所有音频剪辑。
        /// 加载完成后自动播放 BGM；加载失败仅记警告日志。
        /// </summary>
        private async Task LoadClipsAsync()
        {
            // Boot、Main 或其他场景可能先创建音频服务，再注册资源服务。
            // 等待注册完成，避免一次启动时序竞争把音频永久判定为加载失败。
            IGameAssetService assets = null;
            while (!GameApp.TryGet<IGameAssetService>(out assets))
            {
                await Task.Yield();
            }

            // BGM 和音效分别加载。单个音效缺失时不能阻断 BGM 播放。
            bgmClip = await TryLoadClipAsync(assets, "BK");
            sfx[(int)GameSfx.Intensify] = await TryLoadClipAsync(assets, "intensify");
            sfx[(int)GameSfx.Send] = await TryLoadClipAsync(assets, "send");
            sfx[(int)GameSfx.Card] = await TryLoadClipAsync(assets, "Card");
            sfx[(int)GameSfx.ShieldBreak] = await TryLoadClipAsync(assets, "po");
            sfx[(int)GameSfx.Next] = await TryLoadClipAsync(assets, "next");
            sfx[(int)GameSfx.Pass] = await TryLoadClipAsync(assets, "pass");
            loaded = bgmClip != null;
            for (var index = 0; index < sfx.Length; index++)
                loaded |= sfx[index] != null;
            loading = false;

            Debug.Log($"[Audio] 音频加载完成: BK={bgmClip != null}, intensify={sfx[(int)GameSfx.Intensify] != null}, send={sfx[(int)GameSfx.Send] != null}, Card={sfx[(int)GameSfx.Card] != null}, po={sfx[(int)GameSfx.ShieldBreak] != null}, next={sfx[(int)GameSfx.Next] != null}, pass={sfx[(int)GameSfx.Pass] != null}, BgmEnabled={BgmEnabled}, BgmVolume={BgmVolume}");
            PlayBgm();
        }

        private static async Task<AudioClip> TryLoadClipAsync(IGameAssetService assets, string location)
        {
            try
            {
                var clip = await assets.LoadAssetAsync<AudioClip>(location, CancellationToken.None);
                if (clip == null)
                {
                    Debug.LogWarning($"[Audio] 音频资源为空: {location}");
                }

                return clip;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Audio] 音频资源加载失败: {location}, {e.Message}");
                return null;
            }
        }
    }
