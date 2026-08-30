using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 嘲讽表情图 — 挂在表情包 prefab（TauntPrefab）的 TauntEmoji 子物体上（2D Object Sprite）。
    /// 持有嘲讽表情 Sprite 列表，每次表情包播放时由 TauntEmojiController 调用
    /// ApplyRandomSprite 随机抽取一张赋给自身的 SpriteRenderer。
    /// 列表留空时保持 prefab 原图。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class TauntEmojiSprite : MonoBehaviour
    {
        [SerializeField, Tooltip("嘲讽表情 Sprite 列表，每次播放随机抽取一张（留空则保持 prefab 原图）")]
        private Sprite[] emojiSprites;

        private SpriteRenderer _renderer;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>
        /// 从表情池随机抽取一张 Sprite 赋给自身 SpriteRenderer。
        /// 列表大于 1 张时避免与上次抽中同图（由调用方传入上次索引，本组件随实例销毁不存状态）
        /// </summary>
        /// <param name="excludeIndex">上次抽中的索引（避免连续同图），无则传 -1</param>
        /// <returns>本次抽中的索引；列表为空或抽取项为空时返回 -1（保持 prefab 原图）</returns>
        public int ApplyRandomSprite(int excludeIndex = -1)
        {
            if (emojiSprites == null || emojiSprites.Length == 0)
            {
                return -1;
            }

            int index = Random.Range(0, emojiSprites.Length);
            if (emojiSprites.Length > 1 && index == excludeIndex)
            {
                // 顺延一张，避免连续两次同图
                index = (index + 1) % emojiSprites.Length;
            }

            Sprite picked = emojiSprites[index];
            if (picked == null)
            {
                Debug.LogWarning($"[TauntEmojiSprite] 表情池第 {index} 项为空，本次保持 prefab 原图。", this);
                return -1;
            }

            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
            }
            _renderer.sprite = picked;
            return index;
        }
    }
}
