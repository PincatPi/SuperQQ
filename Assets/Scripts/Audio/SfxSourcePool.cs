using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SuperQQ.Audio
{
    /// <summary>
    /// SFX/UI 一次性音效的 AudioSource 对象池（非 MonoBehaviour，由 AudioManager 持有并驱动）。
    ///
    /// 职责：
    ///   - 预分配固定容量 AudioSource，播放时取空闲源，播完自动回收复位，全程零运行时 Instantiate/Destroy；
    ///   - 无空闲源时抢占最早开播者（声音窃取 voice stealing），保证最新音效不被丢弃；
    ///   - 同一 SfxId 最小重播间隔判定（防轰炸），间隔内的重复请求直接丢弃；
    ///   - 支持 3D 定位播放：临时设置 spatialBlend=1 与世界坐标，回收时复位为 2D。
    /// </summary>
    public sealed class SfxSourcePool
    {
        /// <summary>池内单个发声体：音源 + 播放状态 + 回收协程</summary>
        private sealed class PooledSource
        {
            public AudioSource Source;
            public float StartedAt;            // 最近一次开播的 Time.time，用于抢占排序
            public Coroutine ReclaimRoutine;   // 播完回收协程（源被复用时需先取消）
        }

        private readonly List<PooledSource> _sources = new();
        private readonly Dictionary<SfxId, float> _lastPlayTime = new();   // 各音效最近一次实际发声时刻（限频用）
        private readonly MonoBehaviour _runner;                            // 协程宿主（AudioManager）
        private readonly Func<AudioBus, AudioMixerGroup> _groupResolver;   // 总线 → Mixer 分组解析（由 AudioManager 提供）
        private readonly float _spatialMinDistance;                        // 3D 音效最小听距（内不衰减）
        private readonly float _spatialMaxDistance;                        // 3D 音效最大听距（外静音）

        /// <summary>
        /// 创建对象池并在 parent 下预生成全部 AudioSource
        /// </summary>
        /// <param name="parent">池物体挂载父节点（AudioManager 物体）</param>
        /// <param name="capacity">池容量（同时发声上限）</param>
        /// <param name="runner">协程宿主</param>
        /// <param name="groupResolver">总线分组解析器，返回 null 表示直连 AudioListener</param>
        /// <param name="spatialMinDistance">3D 音效最小听距（线性滚降起点，2D 游戏应覆盖画面半宽）</param>
        /// <param name="spatialMaxDistance">3D 音效最大听距（线性滚降终点，超出静音）</param>
        public SfxSourcePool(Transform parent, int capacity, MonoBehaviour runner, Func<AudioBus, AudioMixerGroup> groupResolver,
            float spatialMinDistance = 10f, float spatialMaxDistance = 30f)
        {
            _runner = runner;
            _groupResolver = groupResolver;
            _spatialMinDistance = spatialMinDistance;
            _spatialMaxDistance = Mathf.Max(spatialMaxDistance, spatialMinDistance);

            var poolObj = new GameObject("SfxSourcePool");
            poolObj.transform.SetParent(parent, false);

            for (int i = 0; i < capacity; i++)
            {
                var child = new GameObject($"SfxSource_{i:D2}");
                child.transform.SetParent(poolObj.transform, false);

                var src = child.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;      // 默认 2D，3D 播放时临时置 1

                _sources.Add(new PooledSource { Source = src });
            }
        }

        // ==================== 播放 ====================

        /// <summary>
        /// 播放一条音效条目。
        /// </summary>
        /// <param name="entry">音效配置（须已通过 HasValidClip 校验）</param>
        /// <param name="volumeScale">调用方音量缩放（0~1），与条目音量相乘</param>
        /// <param name="worldPosition">3D 世界坐标；为 null 时按 2D/UI 播放</param>
        /// <returns>是否实际发声（被限频丢弃或 Clip 无效时为 false）</returns>
        public bool Play(SfxEntry entry, float volumeScale, Vector3? worldPosition)
        {
            if (entry == null || !entry.HasValidClip)
            {
                return false;
            }

            // 防轰炸：同一 SfxId 在最小重播间隔内的重复请求直接丢弃
            float now = Time.time;
            if (entry.MinReplayInterval > 0f
                && _lastPlayTime.TryGetValue(entry.Id, out float lastAt)
                && now - lastAt < entry.MinReplayInterval)
            {
                return false;
            }
            _lastPlayTime[entry.Id] = now;

            PooledSource slot = Acquire();

            // 复用/抢占前先取消上一段播放的回收协程，防止旧协程复位本次配置
            if (slot.ReclaimRoutine != null)
            {
                _runner.StopCoroutine(slot.ReclaimRoutine);
                slot.ReclaimRoutine = null;
            }

            AudioSource src = slot.Source;
            src.outputAudioMixerGroup = _groupResolver?.Invoke(entry.Bus);
            src.clip = entry.Clip;
            src.volume = Mathf.Clamp01(entry.Volume * volumeScale);
            if (worldPosition.HasValue)
            {
                src.transform.position = worldPosition.Value;
                src.spatialBlend = 1f;
                // 线性滚降 + 较大最小听距：2D 画面范围内不衰减（默认对数滚降在几米外即明显变小，不适合本视角）
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = _spatialMinDistance;
                src.maxDistance = _spatialMaxDistance;
            }
            else
            {
                src.transform.localPosition = Vector3.zero;
                src.spatialBlend = 0f;
            }

            slot.StartedAt = now;
            src.Play();

            // 播完自动回收复位
            slot.ReclaimRoutine = _runner.StartCoroutine(ReclaimAfter(slot, entry.Clip.length));
            return true;
        }

        // ==================== 内部 ====================

        /// <summary>取一个可用发声体：优先空闲源，无空闲时抢占最早开播者</summary>
        private PooledSource Acquire()
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                if (!_sources[i].Source.isPlaying)
                {
                    return _sources[i];
                }
            }

            // 声音窃取：抢占最早开播的源
            PooledSource oldest = _sources[0];
            for (int i = 1; i < _sources.Count; i++)
            {
                if (_sources[i].StartedAt < oldest.StartedAt)
                {
                    oldest = _sources[i];
                }
            }
            oldest.Source.Stop();
            return oldest;
        }

        /// <summary>延时回收：复位为默认 2D 配置，供下次播放直接使用</summary>
        private IEnumerator ReclaimAfter(PooledSource slot, float delay)
        {
            yield return new WaitForSeconds(delay);

            AudioSource src = slot.Source;
            if (src.isPlaying)
            {
                src.Stop();
            }
            src.clip = null;
            src.volume = 1f;
            src.spatialBlend = 0f;
            src.transform.localPosition = Vector3.zero;
            slot.ReclaimRoutine = null;
        }
    }
}
