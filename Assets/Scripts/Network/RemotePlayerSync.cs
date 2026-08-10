using System.Collections.Generic;
using Minigame.Room.V1;
using SuperQQ.Player;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

namespace SuperQQ.Network
{
    /// <summary>
    /// 远程玩家同步器（挂在非本地玩家的 PlayerController 同物体上）。
    /// 纯表现层驱动：关闭本地状态机与物理模拟，用快照缓冲 + 延迟插值平滑移动。
    ///
    /// 原理：渲染时刻 = 本地时间 - interpolationDelay，始终在最近的两个快照之间 Lerp，
    /// 网络抖动被缓冲吸收，远端角色移动平滑。
    /// </summary>
    public class RemotePlayerSync : MonoBehaviour
    {
        [Header("插值延迟（秒），大于快照间隔即可平滑")]
        [SerializeField] private float interpolationDelay = 0.12f;

        [Header("偏差超过该距离直接瞬移（防长时间追不上）")]
        [SerializeField] private float teleportDistance = 5f;

        [Header("幽灵状态透明度（与本地 PlayerController 默认值一致）")]
        [SerializeField] private float ghostAlpha = 0.5f;

        // 快照缓冲：按接收时间升序
        private struct SnapshotPoint
        {
            public float Time;      // 本地接收时刻
            public Vector2 Pos;
            public Vector2 Dir;
            public int PlayerState; // 0=存活 1=幽灵 2=已通关
            public bool FacingLeft;
        }

        private readonly List<SnapshotPoint> _buffer = new(8);
        private SpriteRenderer _renderer;
        private Color _baseColor;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer != null)
            {
                _baseColor = _renderer.color;
            }

            // 远程玩家由网络驱动，关闭本地逻辑
            var controller = GetComponent<PlayerController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.simulated = false;
                rb.velocity = Vector2.zero;
            }
        }

        /// <summary>由 RoomSnapshotReceiver 在收到快照时调用</summary>
        public void ApplySnapshot(TransformState state)
        {
            if (state?.Position == null) return;

            _buffer.Add(new SnapshotPoint
            {
                Time = UnityEngine.Time.time,
                Pos = new Vector2(state.Position.X, state.Position.Y),
                Dir = state.Direction != null
                    ? new Vector2(state.Direction.X, state.Direction.Y)
                    : Vector2.zero,
                PlayerState = state.PlayerState,
                FacingLeft = state.FacingLeft
            });

            // 缓冲上限：保留最近 1 秒
            while (_buffer.Count > 0 && _buffer[0].Time < UnityEngine.Time.time - 1f)
            {
                _buffer.RemoveAt(0);
            }
        }

        private void Update()
        {
            if (_buffer.Count == 0) return;

            float renderTime = UnityEngine.Time.time - interpolationDelay;

            // 缓冲耗尽（网络卡顿）：停在最新位置
            if (renderTime >= _buffer[_buffer.Count - 1].Time)
            {
                SetPosition(_buffer[_buffer.Count - 1]);
                return;
            }

            // 找到 renderTime 所在的快照区间 [a, b]，在两者之间插值
            for (int i = _buffer.Count - 1; i > 0; i--)
            {
                if (_buffer[i - 1].Time > renderTime) continue;

                SnapshotPoint a = _buffer[i - 1];
                SnapshotPoint b = _buffer[i];
                float span = b.Time - a.Time;
                float t = span > 0.0001f ? (renderTime - a.Time) / span : 1f;
                SetPosition(new SnapshotPoint
                {
                    Pos = Vector2.LerpUnclamped(a.Pos, b.Pos, t),
                    Dir = b.Dir,
                    PlayerState = b.PlayerState,
                    FacingLeft = b.FacingLeft
                });
                return;
            }

            SetPosition(_buffer[0]);
        }

        private void SetPosition(SnapshotPoint point)
        {
            // 偏差过大（复活/重生/严重丢包）直接瞬移，不做插值
            if (Vector2.Distance(transform.position, point.Pos) > teleportDistance)
            {
                transform.position = point.Pos;
            }
            else
            {
                transform.position = new Vector3(point.Pos.x, point.Pos.y, transform.position.z);
            }

            // 朝向：优先按上报的朝向翻转（静止时也正确）；未上报时按输入方向推断
            if (_renderer != null)
            {
                _renderer.flipX = point.FacingLeft;

                // 状态表现：幽灵半透明，其余恢复正常
                Color c = _baseColor;
                c.a = point.PlayerState == 1 ? _baseColor.a * ghostAlpha : _baseColor.a;
                _renderer.color = c;
            }
        }
    }
}
