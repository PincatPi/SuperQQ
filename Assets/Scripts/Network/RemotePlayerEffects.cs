using Minigame.Room.V1;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

namespace SuperQQ.Network
{
    /// <summary>
    /// 远端玩家一次性事件表现（挂在远端化身上）。
    /// 当前实现：死亡时闪红、拾取时闪金，作为占位表现；
    /// 嘲讽事件直接驱动远端 Animator 播放嘲讽动画（与本地 PlayerAnimationController 参数约定一致）；
    /// 后续接入正式音效/粒子时在此扩展（按事件类型播 AudioClip/ParticleSystem）。
    /// </summary>
    public class RemotePlayerEffects : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Animator _animator;
        private RemotePlayerSync _sync;
        private float _flashTimer;
        private Color _flashColor;
        private Color _baseColor;

        // Animator 参数哈希（与 PlayerAnimationController 的约定一致）
        private static readonly int TauntHash = Animator.StringToHash("Taunt");

        private void Awake()
        {
            // 角色 SpriteRenderer 在子物体 Visual 上，需从子级查找（与 PlayerController 一致）
            _renderer = GetComponentInChildren<SpriteRenderer>();
            if (_renderer != null) _baseColor = _renderer.color;
            _animator = GetComponentInChildren<Animator>();
            _sync = GetComponent<RemotePlayerSync>();
        }

        public void Play(PlayerEventType eventType, Vector2 position)
        {
            switch (eventType)
            {
                case PlayerEventType.Die:
                    Flash(new Color(1f, 0.3f, 0.3f));
                    break;
                case PlayerEventType.Hit:
                    Flash(new Color(1f, 0.6f, 0.2f));
                    break;
                case PlayerEventType.Pickup:
                    Flash(new Color(1f, 0.9f, 0.3f));
                    break;
                case PlayerEventType.Jump:
                    // 跳跃动画已由 is_jumping 驱动，无需额外表现
                    break;
                case PlayerEventType.Taunt:
                    // 远端播放嘲讽动画：Trigger 与快照驱动的 VelocityX/bIsJumping 等参数互不冲突，
                    // 打断逻辑由 AnimatorController 过渡决定，与本地表现一致。
                    // 冻结状态屏蔽：本地 PlayTaunt 已在源头上拦截（冻结不上报），此处为防御性校验，
                    // 防止旧版本/异常端发来的嘲讽事件在冻结化身上播放
                    if (_animator != null && !(_sync != null && _sync.BIsFrozen))
                    {
                        _animator.SetTrigger(TauntHash);
                    }
                    break;
            }
        }

        private void Flash(Color color)
        {
            _flashColor = color;
            _flashTimer = 0.15f;
        }

        private void Update()
        {
            if (_flashTimer <= 0f || _renderer == null) return;
            _flashTimer -= Time.deltaTime;
            if (_flashTimer <= 0f)
            {
                _renderer.color = _baseColor;
            }
            else
            {
                _renderer.color = Color.Lerp(_baseColor, _flashColor, _flashTimer / 0.15f);
            }
        }
    }
}
