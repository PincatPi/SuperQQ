using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace SuperQQ.Audio
{
    /// <summary>
    /// 循环淡化通道（非 MonoBehaviour，由 AudioManager 持有并驱动协程）。
    /// 内部双 AudioSource（loop 常开），切换内容时协程交叉淡入淡出。
    /// AudioManager 用它实现 BGM 通道（Music 组）：大厅/关卡背景音乐平滑切换。
    /// </summary>
    public sealed class LoopChannel
    {
        private readonly AudioSource _a;
        private readonly AudioSource _b;
        private readonly MonoBehaviour _runner;   // 协程宿主（AudioManager）

        private AudioSource _active;              // 当前正在发声（或淡入中）的源，无内容时为 null
        private float _targetVolume = 1f;         // 淡入目标音量（条目音量）
        private Coroutine _fadeRoutine;

        /// <summary>当前循环的 Clip（无内容时为 null）</summary>
        public AudioClip CurrentClip => _active != null ? _active.clip : null;

        /// <summary>通道是否有内容正在发声</summary>
        public bool IsPlaying => _active != null && _active.isPlaying;

        /// <summary>
        /// 创建通道并在 parent 下生成双 AudioSource
        /// </summary>
        /// <param name="name">通道物体名（如 MusicChannel）</param>
        /// <param name="parent">挂载父节点（AudioManager 物体）</param>
        /// <param name="output">输出 Mixer 分组，null 表示直连 AudioListener</param>
        /// <param name="runner">协程宿主</param>
        public LoopChannel(string name, Transform parent, AudioMixerGroup output, MonoBehaviour runner)
        {
            _runner = runner;

            var channelObj = new GameObject(name);
            channelObj.transform.SetParent(parent, false);

            _a = CreateSource(channelObj.transform, "A", output);
            _b = CreateSource(channelObj.transform, "B", output);
        }

        private static AudioSource CreateSource(Transform parent, string suffix, AudioMixerGroup output)
        {
            var go = new GameObject($"{parent.name}_{suffix}");
            go.transform.SetParent(parent, false);

            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;              // BGM/环境音均为 2D 非定位
            src.volume = 0f;                    // 初始静默，经淡化进入
            src.outputAudioMixerGroup = output;
            return src;
        }

        // ==================== 播放控制 ====================

        /// <summary>
        /// 交叉淡入新 Clip（旧内容同时淡出）。
        /// 与当前 Clip 相同时仅更新目标音量，不重新淡化。
        /// </summary>
        /// <param name="clip">新循环 Clip，传 null 等价于 Stop</param>
        /// <param name="fadeTime">交叉淡化时长（秒），≤0 时立即切换</param>
        /// <param name="volume">淡入目标音量 0~1（通常取条目音量）</param>
        public void CrossFadeTo(AudioClip clip, float fadeTime, float volume = 1f)
        {
            if (clip == null)
            {
                Stop(fadeTime);
                return;
            }

            // 同一内容重复请求：仅更新音量，避免重启循环
            if (_active != null && _active.clip == clip && _active.isPlaying && _fadeRoutine == null)
            {
                _active.volume = Mathf.Clamp01(volume);
                _targetVolume = Mathf.Clamp01(volume);
                return;
            }

            StartFade(clip, fadeTime, volume);
        }

        /// <summary>淡出并停止通道</summary>
        /// <param name="fadeTime">淡出时长（秒），≤0 时立即停止</param>
        public void Stop(float fadeTime)
        {
            if (_active == null)
            {
                return;
            }
            StartFade(null, fadeTime, 0f);
        }

        /// <summary>重设输出 Mixer 分组（AudioManager 热切换 Mixer 资产时调用）</summary>
        public void SetOutput(AudioMixerGroup output)
        {
            _a.outputAudioMixerGroup = output;
            _b.outputAudioMixerGroup = output;
        }

        // ==================== 内部 ====================

        private void StartFade(AudioClip newClip, float fadeTime, float targetVolume)
        {
            if (_fadeRoutine != null)
            {
                _runner.StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }
            _fadeRoutine = _runner.StartCoroutine(FadeRoutine(newClip, fadeTime, Mathf.Clamp01(targetVolume)));
        }

        /// <summary>
        /// 交叉淡化协程：旧源淡出停止，新源自零淡入。
        /// 使用 unscaledDeltaTime，保证游戏暂停（timeScale=0）期间 UI 音乐切换仍然生效。
        /// </summary>
        private IEnumerator FadeRoutine(AudioClip newClip, float fadeTime, float targetVolume)
        {
            AudioSource fadeOut = _active;
            AudioSource fadeIn = null;

            if (newClip != null)
            {
                fadeIn = _active == _a ? _b : _a;
                fadeIn.clip = newClip;
                fadeIn.volume = 0f;
                fadeIn.Play();
                _active = fadeIn;
                _targetVolume = targetVolume;
            }

            if (fadeTime <= 0f)
            {
                // 立即切换
                if (fadeOut != null)
                {
                    fadeOut.Stop();
                    fadeOut.clip = null;
                    fadeOut.volume = 0f;
                }
                if (fadeIn != null)
                {
                    fadeIn.volume = targetVolume;
                }
            }
            else
            {
                float outStart = fadeOut != null ? fadeOut.volume : 0f;
                float elapsed = 0f;
                while (elapsed < fadeTime)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeTime);
                    if (fadeOut != null)
                    {
                        fadeOut.volume = Mathf.Lerp(outStart, 0f, t);
                    }
                    if (fadeIn != null)
                    {
                        fadeIn.volume = Mathf.Lerp(0f, targetVolume, t);
                    }
                    yield return null;
                }
                if (fadeOut != null)
                {
                    fadeOut.Stop();
                    fadeOut.clip = null;
                    fadeOut.volume = 0f;
                }
            }

            // 纯停止路径：通道清空
            if (newClip == null)
            {
                _active = null;
            }
            _fadeRoutine = null;
        }
    }
}
