using System;
using UnityEngine;

namespace SuperQQ.Audio
{
    /// <summary>
    /// 单条音效配置（AudioCatalog 的条目）。
    /// 描述一个 SfxId 的播放方式：Clip、输出总线、音量、最小重播间隔。
    /// 纯数据类，由策划/音频在 Inspector 中配置，运行时只读。
    /// </summary>
    [Serializable]
    public class SfxEntry
    {
        [Tooltip("音效标识；同一 AudioCatalog 内不允许重复（OnValidate 会检查）")]
        public SfxId Id = SfxId.None;

        [Tooltip("音频片段")]
        public AudioClip Clip;

        [Tooltip("输出总线分组：UI 音效选 UI，场景玩法音效选 SFX，BGM 选 Music")]
        public AudioBus Bus = AudioBus.SFX;

        [Tooltip("播放音量 0~1（再乘调用方的音量缩放）")]
        [Range(0f, 1f)] public float Volume = 1f;

        [Header("播放保护")]
        [Tooltip("同一音效的最小重播间隔（秒）；间隔内的重复请求直接丢弃，防止同帧批量触发（如多物品同时拾取）造成音量轰炸")]
        [Min(0f)] public float MinReplayInterval = 0.05f;

        /// <summary>是否存在可播放的 Clip</summary>
        public bool HasValidClip => Clip != null;
    }
}
