using System.Collections.Generic;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Network
{
    /// <summary>
    /// 远端金币跟随（纯表现）：金币被远端玩家认领后，沿远端化身近期轨迹延迟跟随，
    /// 与本地 CoinFollowGroup 的视觉效果一致（延迟平滑尾随）。
    /// 跟随目标为远端化身的 CoinsFollowPoint 锚点（与本地同一参照，含身后距离与高度）；
    /// 锚点位置由 RemotePlayerSync 快照插值 + CoinsFollowPointFlip 朝向翻转共同驱动，本组件记录其轨迹。
    /// 跟随的化身出局（幽灵/通关，经快照 player_state 判定）时金币消失。
    /// </summary>
    public class RemoteCoinFollow : MonoBehaviour
    {
        [SerializeField] private float followSmoothTime = 0.08f;
        [SerializeField] private float trailSampleInterval = 0.05f;

        [Tooltip("队列错峰间隔（秒）：与本地 Coin.spacingDelay 一致，第 N 枚金币额外延迟 N×该时长")]
        [SerializeField] private float spacingDelay = 0.12f;

        private Transform _target;
        private float _delay;
        private Vector2 _offset;
        private Vector2 _velocity;
        private float _sampleTimer;

        // 远端化身轨迹（时间 + 位置），用于延迟取点
        private readonly List<(float time, Vector2 pos)> _trail = new(64);

        // 每个远端化身的跟随队列：第 N 枚金币取位次 N，错峰延迟 N×spacingDelay
        private static readonly Dictionary<Transform, List<RemoteCoinFollow>> _groups = new();

        public void Init(Transform target, float delay, Vector2 offset)
        {
            _target = target;
            _offset = offset;

            // 入队取位次：与本地 EffectiveDelay(slot) = followDelay + slot × spacingDelay 对齐
            if (!_groups.TryGetValue(target, out List<RemoteCoinFollow> queue))
            {
                queue = new List<RemoteCoinFollow>();
                _groups[target] = queue;
            }
            queue.Add(this);
            _delay = delay + (queue.Count - 1) * spacingDelay;
        }

        private void OnDestroy()
        {
            if (_target != null && _groups.TryGetValue(_target, out List<RemoteCoinFollow> queue))
            {
                queue.Remove(this);
                if (queue.Count == 0)
                {
                    _groups.Remove(_target);
                }
            }
        }

        private void Update()
        {
            if (_target == null)
            {
                Destroy(gameObject);
                return;
            }

            // 跟随者出局（幽灵/通关）：金币消失（远端化身 player_state 由 RemotePlayerSync 反映在透明度/隐藏上，
            // 这里直接读注册表状态；读不到时不做限制）
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry != null)
            {
                PlayerController player = _target.GetComponentInParent<PlayerController>();
                if (player != null)
                {
                    PlayerStateType state = registry.GetPlayerState(player);
                    if (state == PlayerStateType.Ghost || state == PlayerStateType.Finished)
                    {
                        Destroy(gameObject);
                        return;
                    }
                }
            }

            // 采样轨迹
            _sampleTimer += Time.deltaTime;
            if (_sampleTimer >= trailSampleInterval)
            {
                _sampleTimer = 0f;
                _trail.Add((Time.time, _target.position));
                while (_trail.Count > 0 && _trail[0].time < Time.time - _delay - 1f)
                {
                    _trail.RemoveAt(0);
                }
            }

            // 取 delay 秒前的轨迹点作为跟随目标
            Vector2 followPos = _target.position;
            float targetTime = Time.time - _delay;
            for (int i = _trail.Count - 1; i >= 0; i--)
            {
                if (_trail[i].time <= targetTime)
                {
                    followPos = _trail[i].pos;
                    break;
                }
            }

            Vector2 pos = Vector2.SmoothDamp(transform.position, followPos + _offset, ref _velocity, followSmoothTime);
            transform.position = new Vector3(pos.x, pos.y, transform.position.z);
        }
    }
}
