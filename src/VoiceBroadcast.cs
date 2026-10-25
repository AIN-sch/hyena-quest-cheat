using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace HyenaQuestCheat
{
    /// <summary>语音广播：本地音频覆盖麦克风输入广播给全房；本地低音量监听。</summary>
    /// AudioClip/AudioSource 走反射（Unity6 的 AudioModule 需 netstandard2.1，net472 工程编译不过；运行时类型都在）。
    public static class VoiceBroadcast
    {
        public static string AudioPath = "";       // 音频文件路径（本地文件或 http 地址）
        public static bool Loop;                    // 播完循环
        public static float MonitorVolume = 0.15f;  // 本地监听音量（队友按各自音量正常听到）
        public static bool Active;                  // 正在广播
        public static string Status = "未加载";

        private static float[] _pcm;       // 48kHz 单声道，供注入
        private static int _pos;           // 注入读指针
        private static object _clip;       // AudioClip（反射）
        private static object _monitor;    // AudioSource（反射）
        private static bool _loading;
        private static string _loadPath = "";
        private static float _lastFrameAt = float.NegativeInfinity;   // 最近一次本地语音帧时间

        /// <summary>语音管线是否在跑：本地 MetaVc 每帧回调 SendFrame，1秒内没帧即视为离线。</summary>
        public static bool VoiceOnline => Time.time - _lastFrameAt < 1f;

        /// <summary>Harmony Prefix 每帧调用，标记语音帧活跃。</summary>
        public static void TouchFrame() => _lastFrameAt = Time.time;

        // ---------- 反射缓存：UnityWebRequest / AudioClip / AudioSource ----------
        private static readonly Type T_AudioType = FindType("UnityEngine.AudioType");
        private static readonly MethodInfo M_GetAudioClip =
            T_AudioType == null ? null : FindType("UnityEngine.Networking.UnityWebRequestMultimedia")?.GetMethod(
                "GetAudioClip", new[] { typeof(string), T_AudioType });
        private static readonly PropertyInfo P_HandlerClip = FindType("UnityEngine.Networking.DownloadHandlerAudioClip")?.GetProperty("audioClip");
        private static readonly PropertyInfo P_Samples = FindType("UnityEngine.AudioClip")?.GetProperty("samples");
        private static readonly PropertyInfo P_Channels = FindType("UnityEngine.AudioClip")?.GetProperty("channels");
        private static readonly PropertyInfo P_Frequency = FindType("UnityEngine.AudioClip")?.GetProperty("frequency");
        private static readonly MethodInfo M_GetData = FindType("UnityEngine.AudioClip")?.GetMethod("GetData", new[] { typeof(float[]), typeof(int) });

        private static Type _srcType;              // UnityEngine.AudioSource
        private static PropertyInfo _srcVol, _srcClip, _srcLoop, _srcPlayOnAwake;
        private static MethodInfo _srcPlay, _srcStop;

        /// <summary>每帧调用：对局外自动停；监听音量跟随。</summary>
        public static void Update()
        {
            if (!Active) return;
            if (!VoiceOnline) { Stop("已停止（对局外）"); return; }
            if (_monitor == null) return;
            EnsureSrcRefs();
            _srcVol?.SetValue(_monitor, MonitorVolume, null);
            _srcLoop?.SetValue(_monitor, Loop, null);
        }

        public static void Toggle()
        {
            if (Active) Stop();
            else Start();
        }

        /// <summary>开始广播：已加载则直接播，路径变化则先异步加载。</summary>
        public static void Start()
        {
            if (Active) { Features.Notify("语音广播 播放中"); return; }
            if (_loading) { Features.Notify("音频加载中..."); return; }
            if (string.IsNullOrWhiteSpace(AudioPath)) { Features.Notify("先填音频文件路径"); return; }
            if (!VoiceOnline) { Status = "对局外"; Features.Notify("语音通道未开启，进对局后再播"); return; }
            if (_clip == null || _loadPath != AudioPath)
            {
                _loadPath = AudioPath;
                LoadAsync(AudioPath);
                return;
            }
            BeginPlay();
        }

        /// <summary>强制重载当前路径（改了文件内容后重播）。</summary>
        public static void Reload()
        {
            if (_loading) return;
            Stop();
            _clip = null;
            _pcm = null;
            _loadPath = "";
            Start();
        }

        public static void Stop(string reason = "已停止")
        {
            Active = false;
            if (_monitor != null)
            {
                EnsureSrcRefs();
                _srcStop?.Invoke(_monitor, null);
            }
            Status = reason;
        }

        /// <summary>Harmony Prefix 调用：覆盖麦克风采样，队友听到本地音频。</summary>
        public static void InjectSamples(float[] samples)
        {
            if (!Active || samples == null || _pcm == null || _pcm.Length == 0) return;
            for (int i = 0; i < samples.Length; i++)
            {
                if (_pos >= _pcm.Length)
                {
                    if (Loop) { _pos = 0; }
                    else
                    {
                        Active = false;
                        if (_monitor != null) { EnsureSrcRefs(); _srcStop?.Invoke(_monitor, null); }
                        Status = "已播完";
                        for (int k = i; k < samples.Length; k++) samples[k] = 0f;   // 收尾清余，不漏真实麦克风
                        return;
                    }
                }
                samples[i] = _pcm[_pos++];
            }
        }

        private static void LoadAsync(string path)
        {
            _loading = true;
            Status = "加载中...";
            Plugin.Instance.StartCoroutine(CoLoad(path));
        }

        private static IEnumerator CoLoad(string path)
        {
            string url;
            if (File.Exists(path)) url = "file://" + path.Replace('\\', '/');
            else if (path.StartsWith("http://") || path.StartsWith("https://")) url = path;
            else { Features.Notify("文件不存在"); Status = "文件不存在"; _loading = false; yield break; }
            if (M_GetAudioClip == null || T_AudioType == null)
            {
                Features.Notify("音频接口不可用"); Status = "不可用"; _loading = false;
                yield break;
            }
            var uwr = (UnityWebRequest)M_GetAudioClip.Invoke(null, new object[] { url, Enum.ToObject(T_AudioType, 0) });
            yield return uwr.SendWebRequest();
            if (!string.IsNullOrEmpty(uwr.error))
            {
                Features.Notify("音频加载失败: " + uwr.error);
                Status = "加载失败";
                _loading = false;
                yield break;
            }
            object clip = uwr.downloadHandler != null && P_HandlerClip != null
                ? P_HandlerClip.GetValue(uwr.downloadHandler) : null;
            if (clip == null) { Features.Notify("音频解码失败"); Status = "解码失败"; _loading = false; yield break; }
            _clip = clip;
            _pcm = ToMono48k(clip);
            _loading = false;
            if (_pcm == null || _pcm.Length == 0) { Features.Notify("音频数据为空"); Status = "无数据"; yield break; }
            Status = "已加载 " + Path.GetFileName(path);
            BeginPlay();
        }

        private static void BeginPlay()
        {
            if (!VoiceOnline) { Status = "对局外"; Features.Notify("语音通道已断开，未开始广播"); return; }
            if (_pcm == null) { Features.Notify("音频未就绪"); return; }
            _pos = 0;
            Active = true;
            EnsureMonitor();
            if (_monitor != null)
            {
                EnsureSrcRefs();
                _srcClip?.SetValue(_monitor, _clip, null);
                _srcVol?.SetValue(_monitor, MonitorVolume, null);
                _srcLoop?.SetValue(_monitor, Loop, null);
                _srcPlay?.Invoke(_monitor, null);
            }
            Status = "广播中";
            Features.Notify("语音广播 开始");
        }

        private static void EnsureMonitor()
        {
            if (_monitor != null) return;
            _srcType = FindType("UnityEngine.AudioSource");
            if (_srcType == null) return;
            var go = new GameObject("VoiceBroadcastMonitor");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _monitor = go.AddComponent(_srcType);
            EnsureSrcRefs();
            _srcPlayOnAwake?.SetValue(_monitor, false, null);
        }

        private static void EnsureSrcRefs()
        {
            if (_srcVol != null || _srcType == null) return;
            _srcVol = _srcType.GetProperty("volume");
            _srcClip = _srcType.GetProperty("clip");
            _srcLoop = _srcType.GetProperty("loop");
            _srcPlayOnAwake = _srcType.GetProperty("playOnAwake");
            _srcPlay = _srcType.GetMethod("Play", Type.EmptyTypes);
            _srcStop = _srcType.GetMethod("Stop", Type.EmptyTypes);
        }

        /// <summary>按全名在已加载程序集里找类型（引擎模块运行时都在）。</summary>
        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>AudioClip → 48kHz 单声道 float PCM（Voice 帧格式：48000/单声道/20ms=960采样）。</summary>
        private static float[] ToMono48k(object clip)
        {
            if (clip == null || M_GetData == null || P_Samples == null || P_Channels == null || P_Frequency == null) return null;
            int samples = (int)P_Samples.GetValue(clip);
            int ch = Mathf.Max(1, (int)P_Channels.GetValue(clip));
            int srcRate = (int)P_Frequency.GetValue(clip);
            if (samples <= 0 || srcRate <= 0) return null;
            var src = new float[samples * ch];
            if (!(bool)M_GetData.Invoke(clip, new object[] { src, 0 })) return null;

            // 降混为单声道（按 clip 原始采样率）
            var mono = new float[samples];
            if (ch == 1) Array.Copy(src, mono, samples);
            else for (int i = 0; i < samples; i++)
            {
                float sum = 0f;
                for (int c = 0; c < ch; c++) sum += src[i * ch + c];
                mono[i] = sum / ch;
            }

            // 线性插值重采样到 48000
            if (srcRate == 48000) return mono;
            long outLen = (long)samples * 48000L / srcRate;
            var outp = new float[outLen];
            for (long i = 0; i < outLen; i++)
            {
                float t = (float)((double)i * srcRate / 48000.0);
                int i0 = (int)t;
                int i1 = Math.Min(i0 + 1, samples - 1);
                float frac = t - i0;
                outp[i] = mono[i0] + (mono[i1] - mono[i0]) * frac;
            }
            return outp;
        }
    }
}
