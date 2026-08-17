using Minigame.Room.V1;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

namespace SuperQQ.Network
{
    /// <summary>
    /// 远端玩家一次性事件表现（挂在远端化身上）。
    /// 当前实现：死亡时闪红、拾取时闪金，作为占位表现；
    /// 后续接入正式音效/粒子时在此扩展（按事件类型播 AudioClip/ParticleSystem）。
    /// </summary>
    public class RemotePlayerEffects : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private float _flashTimer;
        private Color _flashColor;
        private Color _baseColor;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer != null) _baseColor = _renderer.color;
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
