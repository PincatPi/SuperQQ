using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 嘲讽表情包播放器 — 挂在 PlayerController 同物体上（本地与远端化身均可挂载）。
    /// 嘲讽时在化身右上方偏移处实例化表情包 prefab（prefab 自带 Animator：
    /// 默认状态 TauntShow / 收尾状态 TauntClose / Trigger 参数 Close）：
    ///   - 实例化后由 prefab Animator 默认状态自动播放 TauntShow 弹出动画
    ///   - 固定时长（Duration）到期后代码置 Close Trigger 播放 TauntClose 收尾动画，播完销毁
    ///   - 重复嘲讽立即销毁旧实例并重新实例化（打断表现为"取消旧的、播放新的"）
    ///   - 表情图随机：由 prefab 内 TauntEmoji 子物体上的 TauntEmojiSprite 组件负责
    ///     （Sprite 列表配置在该组件上），实例化后本组件驱动其随机抽图
    /// 表情包 prefab 在 Inspector 中配置；本地由 PlayerAnimationController 调用，
    /// 远端由 RemotePlayerEffects 调用，两端表现一致。
    /// 注意：prefab 的 TauntClose 动画需取消 Loop Time，否则无法判定播放完毕（由超时兜底销毁）。
    /// </summary>
    public class TauntEmojiController : MonoBehaviour
    {
        [Header("表情包")]
        [SerializeField, Tooltip("表情包 prefab（自带 Animator：TauntShow/TauntClose + Close Trigger）")]
        private GameObject emojiPrefab;
        [SerializeField, Tooltip("相对化身的生成偏移（右上方）")]
        private Vector2 spawnOffset = new Vector2(0.5f, 0.8f);
        [SerializeField, Tooltip("TauntShow 播放固定时长（秒），到期置 Close Trigger 播 TauntClose 收尾")]
        private float duration = 2f;
        [SerializeField, Tooltip("TauntClose 收尾动画的最大等待时长（秒，安全兜底，大于实际动画时长即可）")]
        private float closeMaxDuration = 2f;

        // Animator 参数哈希（与表情包 prefab 的 Trigger 参数约定一致）
        private static readonly int CloseHash = Animator.StringToHash("Close");

        private GameObject _current;            // 当前表情包实例
        private Animator _currentAnimator;      // 当前实例的 Animator（驱动 Close 收尾）
        private float _showEndTime;             // TauntShow 到期时刻（届时置 Close Trigger）
        private float _closeTimeoutTime;        // 收尾动画超时兜底销毁时刻
        private bool _closing;                  // 是否已进入 TauntClose 收尾阶段
        private int _lastSpriteIndex = -1;      // 上次抽中的表情索引（避免连续两次同图；实例会销毁，状态留在本组件）

        /// <summary>
        /// 播放表情包：已有实例立即销毁（打断），重新实例化，由 prefab Animator 自动播放 TauntShow
        /// </summary>
        public void Play()
        {
            if (emojiPrefab == null)
            {
                Debug.LogWarning("[TauntEmojiController] 未配置表情包 prefab，无法播放嘲讽表情包。请在 Inspector 中设置 Emoji Prefab。", this);
                return;
            }

            CancelCurrentImmediate();

            _current = Instantiate(emojiPrefab, transform);
            _current.transform.localPosition = new Vector3(spawnOffset.x, spawnOffset.y, 0f);

            // 表情图随机：驱动实例内 TauntEmoji 子物体上的 TauntEmojiSprite 抽图（未挂该组件则保持 prefab 原图）
            TauntEmojiSprite emojiSprite = _current.GetComponentInChildren<TauntEmojiSprite>(true);
            if (emojiSprite != null)
            {
                _lastSpriteIndex = emojiSprite.ApplyRandomSprite(_lastSpriteIndex);
            }

            _currentAnimator = _current.GetComponentInChildren<Animator>();
            if (_currentAnimator == null)
            {
                Debug.LogWarning("[TauntEmojiController] 表情包 prefab 未找到 Animator，TauntClose 收尾动画不会播放，到期将直接销毁。", _current);
            }

            _showEndTime = Time.time + duration;
            _closing = false;
        }

        /// <summary>立即销毁当前表情包实例（重复嘲讽打断 / 收尾完毕回收共用）</summary>
        private void CancelCurrentImmediate()
        {
            if (_current != null)
            {
                Destroy(_current);
                _current = null;
                _currentAnimator = null;
            }
        }

        private void Update()
        {
            if (_current == null)
            {
                return;
            }

            // TauntShow 阶段：到期后置 Close Trigger 进入收尾
            if (!_closing)
            {
                if (Time.time >= _showEndTime)
                {
                    _closing = true;
                    _closeTimeoutTime = Time.time + closeMaxDuration;
                    if (_currentAnimator != null)
                    {
                        _currentAnimator.SetTrigger(CloseHash);
                    }
                }
                return;
            }

            // 收尾阶段：TauntClose 播完销毁；超时兜底（动画缺失/循环配置错误时也能回收）
            if (Time.time >= _closeTimeoutTime || BIsCloseAnimFinished())
            {
                CancelCurrentImmediate();
            }
        }

        /// <summary>TauntClose 是否已播放完毕（无 Animator 时视为完毕，直接销毁）</summary>
        private bool BIsCloseAnimFinished()
        {
            if (_currentAnimator == null)
            {
                return true;
            }
            AnimatorStateInfo state = _currentAnimator.GetCurrentAnimatorStateInfo(0);
            return state.IsName("TauntClose") && state.normalizedTime >= 1f && !_currentAnimator.IsInTransition(0);
        }
    }
}
