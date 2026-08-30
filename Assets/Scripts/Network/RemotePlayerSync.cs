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
        [SerializeField] private float interpolationDelay = 0.07f;

        [Header("缓冲耗尽时按速度外推的最长时间（秒）")]
        [SerializeField] private float maxExtrapolationTime = 0.25f;

        [Header("偏差超过该距离直接瞬移（防长时间追不上）")]
        [SerializeField] private float teleportDistance = 5f;

        [Header("幽灵状态透明度（与本地 PlayerController 默认值一致）")]
        [SerializeField] private float ghostAlpha = 0.5f;

        [Header("死亡动画过渡时长（秒，0=自动读 PlayerController.DeathDuration）")]
        [SerializeField] private float deathTransitionDuration = 0f;

        // 实际生效的死亡过渡时长
        private float EffectiveDeathDuration
        {
            get
            {
                if (deathTransitionDuration > 0f) return deathTransitionDuration;
                PlayerController pc = GetComponent<PlayerController>();
                return pc != null ? pc.DeathDuration : 2f;
            }
        }

        // 远端进入幽灵表现的时刻（收到首个 player_state=1 时起算，期间播死亡动画）
        private float _ghostEnterTime = -1f;
        private bool _wasGhost;

        // 远端冻结视觉（快照 player_state=3 时挂载冰封特效，解除后消散销毁）
        private FrozenIceEffect _frozenVisual;

        // 远端当前是否处于冻结状态（快照 player_state=3），供 RemotePlayerEffects 屏蔽冻结期间的嘲讽事件
        public bool BIsFrozen { get; private set; }

        // 快照缓冲：按接收时间升序
        private struct SnapshotPoint
        {
            public float Time;      // 本地接收时刻
            public Vector2 Pos;
            public Vector2 Vel;     // 上报速度，用于外推与动画
            public Vector2 Dir;
            public int PlayerState; // 0=存活 1=幽灵 2=已通关
            public bool FacingLeft;
            public bool IsJumping;  // 滞空（跳跃/坠落），驱动 Animator bIsJumping
        }

        private readonly List<SnapshotPoint> _buffer = new(8);
        private SpriteRenderer _renderer;
        private Color _baseColor;
        private Animator _animator;
        private PlayerAnimationController _localAnimDriver;

        // Animator 参数哈希（与 PlayerAnimationController 的约定一致）
        private static readonly int VelocityXHash = Animator.StringToHash("VelocityX");
        private static readonly int VelocityYHash = Animator.StringToHash("VelocityY");
        private static readonly int IsDeadHash = Animator.StringToHash("bIsDead");
        private static readonly int IsJumpingHash = Animator.StringToHash("bIsJumping");
        private static readonly int IsVictoryHash = Animator.StringToHash("bIsVictory");
        private static readonly int IsGhostHash = Animator.StringToHash("bIsGhost");

        private void Awake()
        {
            // 角色 SpriteRenderer 在子物体 Visual 上，需从子级查找（与 PlayerController 一致）
            _renderer = GetComponentInChildren<SpriteRenderer>();
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

            // 关闭本地动画驱动器（远端动画改由本组件按快照数据直接驱动，见 SetPosition）
            _localAnimDriver = GetComponent<PlayerAnimationController>();
            if (_localAnimDriver != null)
            {
                _localAnimDriver.enabled = false;
            }
            _animator = GetComponentInChildren<Animator>();

            // 关闭碰撞体：远端化身不参与本端物理与道具触发
            // （道具效果只作用于各端自己的本地玩家，效果经受害者自己的状态上报广播）
            foreach (Collider2D col in GetComponentsInChildren<Collider2D>(true))
            {
                col.enabled = false;
            }
        }

        /// <summary>由 RoomSnapshotReceiver 在收到快照时调用</summary>
        public void ApplySnapshot(TransformState state)
        {
            if (state?.Position == null) return;

            // 玩家从未上报过状态时，服务器缓存的位置是无效的零值（proto 默认 0,0），
            // 应用会把化身瞬移到原点并与其他玩家重叠——state_seq=0 即"从未上报"，跳过。
            if (state.StateSeq == 0) return;

            _buffer.Add(new SnapshotPoint
            {
                Time = UnityEngine.Time.time,
                Pos = new Vector2(state.Position.X, state.Position.Y),
                Vel = state.Velocity != null
                    ? new Vector2(state.Velocity.X, state.Velocity.Y)
                    : Vector2.zero,
                Dir = state.Direction != null
                    ? new Vector2(state.Direction.X, state.Direction.Y)
                    : Vector2.zero,
                PlayerState = state.PlayerState,
                FacingLeft = state.FacingLeft,
                IsJumping = state.IsJumping
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

            // 缓冲耗尽（网络卡顿）：按最新快照的速度外推一小段时间（Dead Reckoning），
            // 比停在原地等包更跟手；外推时间钳制上限，防止跑飞
            if (renderTime >= _buffer[_buffer.Count - 1].Time)
            {
                SnapshotPoint latest = _buffer[_buffer.Count - 1];
                float extra = Mathf.Min(renderTime - latest.Time, maxExtrapolationTime);
                latest.Pos += latest.Vel * extra;
                SetPosition(latest);
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
                    Vel = b.Vel,
                    Dir = b.Dir,
                    PlayerState = b.PlayerState,
                    FacingLeft = b.FacingLeft,
                    IsJumping = b.IsJumping
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

                // 状态表现：幽灵半透明，其余恢复正常。
                // 基于当前颜色只调 alpha（保留玩家座位色的 RGB），不用 Awake 时的旧色，
                // 避免把 ApplyProfile 设置的玩家色覆盖、且 alpha 算在过期颜色上。
                Color c = _renderer.color;
                float baseAlpha = _baseColor.a;
                c.a = point.PlayerState == 1 ? baseAlpha * ghostAlpha : baseAlpha;
                _renderer.color = c;
            }

            // 动画：按快照数据直接驱动远端 Animator（与本地 PlayerAnimationController 参数约定一致）。
            // 死亡表现与本地一致：收到 player_state=1 后先播死亡动画（躺），过渡时长后退出死亡动画
            // 进入幽灵表现（半透明 + 幽灵动画），避免 bIsDead 恒 true 导致死亡动画卡在最后一帧。
            bool isGhost = point.PlayerState == 1;
            if (isGhost && !_wasGhost)
            {
                _ghostEnterTime = UnityEngine.Time.time;   // 首次进入幽灵，开始死亡动画过渡
            }
            if (!isGhost)
            {
                _ghostEnterTime = -1f;
            }
            _wasGhost = isGhost;

            // 死亡动画仅在过渡期内为 true；过渡期后退出（与本地 Dying→Ghost 一致）
            bool playingDeath = isGhost
                && _ghostEnterTime >= 0f
                && (UnityEngine.Time.time - _ghostEnterTime) < EffectiveDeathDuration;

            if (_animator != null)
            {
                _animator.SetFloat(VelocityXHash, Mathf.Abs(point.Vel.x));
                _animator.SetFloat(VelocityYHash, point.Vel.y);
                _animator.SetBool(IsDeadHash, playingDeath);
                _animator.SetBool(IsJumpingHash, point.IsJumping);
                // 通关表现与本地一致：player_state=2 时循环播放胜利动画（通关端速度为 0，无须屏蔽移动动画）
                _animator.SetBool(IsVictoryHash, point.PlayerState == 2);
                // 幽灵动画与本地时序一致：死亡过渡（bIsDead）结束后才进入幽灵动画，复活后自动置回 false
                _animator.SetBool(IsGhostHash, isGhost && !playingDeath);
            }

            // 冻结状态视觉（液氮事件）：快照 player_state=3 时挂载冰封特效，解除后消散。
            // 冻结玩家的移动停止由拥有者本端上报的静止位置自然体现，此处只管视觉
            BIsFrozen = point.PlayerState == 3;
            UpdateFrozenVisual(BIsFrozen);
        }

        /// <summary>
        /// 远端玩家冻结视觉：快照上报冻结状态时挂载冰封特效（复用液氮事件资产配置的 prefab），
        /// 状态解除后调用 Dissipate 自然消散并延迟销毁。特效挂为化身子节点随其移动。
        /// </summary>
        private void UpdateFrozenVisual(bool bFrozen)
        {
            if (bFrozen && _frozenVisual == null)
            {
                SuperQQ.Event.LiquidNitrogenLeakModifier modifier = SuperQQ.Event.LiquidNitrogenLeakModifier.ActiveInstance;
                if (modifier != null && modifier.IceBlockPrefab != null)
                {
                    _frozenVisual = Instantiate(modifier.IceBlockPrefab, transform);
                }
            }
            else if (!bFrozen && _frozenVisual != null)
            {
                _frozenVisual.Dissipate();
                SuperQQ.Event.LiquidNitrogenLeakModifier modifier = SuperQQ.Event.LiquidNitrogenLeakModifier.ActiveInstance;
                Destroy(_frozenVisual.gameObject, modifier != null ? modifier.IceEffectDestroyDelay : 0.6f);
                _frozenVisual = null;
            }
        }

        private void OnDestroy()
        {
            if (_frozenVisual != null)
            {
                Destroy(_frozenVisual.gameObject);
                _frozenVisual = null;
            }
        }
    }
}
