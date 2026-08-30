using System.Collections.Generic;
using Cinemachine;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.CameraControl
{
    /// <summary>
    /// 相机目标组管理器 — 关卡场景中放置一个，配合 CinemachineTargetGroup + GroupComposer 使用
    /// 订阅 LevelPlayerRegistry 的玩家集合/状态变化事件，自动维护 TargetGroup 成员
    /// 使 Cinemachine 镜头始终框住应被关注的玩家：
    /// - 存活玩家（本地 + 远端，含晚进房补生成的）
    /// - 幽灵状态的本地玩家（仍在操控移动，镜头需跟随）
    /// 出局玩家（远端幽灵/任意通关）会被移出目标组
    ///
    /// 另负责通关观战镜头：本地玩家通关后被移出目标组时，将主镜头平滑切换至
    /// 固定观战镜头（通常为 SelectionPlacementCamera），复活后平滑切回游玩镜头
    ///
    /// 设计说明：
    /// - 不依赖网络层，玩家来源统一走 LevelPlayerRegistry（本地/远端/晚进房玩家都会经过它注册）
    /// - 通过事件驱动，NetworkManager / RoomSnapshotReceiver 等无需感知本组件存在
    /// - 扩展点：重写 ShouldTrack 调整过滤规则，重写 CollectTargets 自定义目标来源
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraTargetGroupManager : MonoBehaviour
    {
        [Header("目标组")]
        [SerializeField] private CinemachineTargetGroup _targetGroup;    // 场景中的 CinemachineTargetGroup，为空时自动查找

        [Header("目标参数")]
        [SerializeField] private float _targetWeight = 1f;               // 每个玩家在镜头构图中的权重
        [SerializeField] private float _targetRadius = 1f;               // 每个玩家的包围半径，保证角色四周留边距

        [Header("通关观战镜头")]
        [Tooltip("本地玩家通关后接管主镜头的固定视角 Virtual Camera（通常为场景中的 SelectionPlacementCamera）；留空则通关后仍跟随目标组")]
        [SerializeField] private CinemachineVirtualCamera _finishedSpectatorCamera;
        [Tooltip("通关观战镜头生效时的优先级，需高于游玩镜头（默认 10），与选择/放置阶段镜头同级")]
        [SerializeField] private int _finishedSpectatorCameraPriority = 20;

        // 观战镜头接管状态（仅当本地玩家处于通关状态时生效）
        private bool _bSpectatorCameraActive;
        private int _spectatorCameraOriginalPriority;

        // 已入组的玩家 Transform，用于去重（TargetGroup 自身不去重）
        private readonly HashSet<Transform> _addedTargets = new();

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (_targetGroup == null)
            {
                _targetGroup = FindFirstObjectByType<CinemachineTargetGroup>();
            }

            if (_targetGroup == null)
            {
                Debug.LogError("[CameraTargetGroupManager] 场景中找不到 CinemachineTargetGroup，请检查配置。");
                enabled = false;
            }
        }

        private void Start()
        {
            // 订阅玩家集合变化：进房批量生成、晚进房补生成、玩家销毁都会触发
            if (LevelPlayerRegistry.Instance != null)
            {
                LevelPlayerRegistry.Instance.OnPlayersChanged += RebuildTargets;
                LevelPlayerRegistry.Instance.OnPlayerStateChanged += HandlePlayerStateChanged;
            }

            // 兜底一次全量重建：本组件与 Registry 的 Start 执行顺序不定
            // 若 Registry 先完成生成则此处补齐，若本组件先执行则由事件覆盖后续生成
            RebuildTargets();
        }

        private void OnDestroy()
        {
            if (LevelPlayerRegistry.Instance != null)
            {
                LevelPlayerRegistry.Instance.OnPlayersChanged -= RebuildTargets;
                LevelPlayerRegistry.Instance.OnPlayerStateChanged -= HandlePlayerStateChanged;
            }

            // 兜底：本组件先销毁时还原观战镜头优先级（Unity 重载 == 可判已销毁对象，此处为异场景引用防御）
            if (_bSpectatorCameraActive && _finishedSpectatorCamera != null)
            {
                _finishedSpectatorCamera.Priority = _spectatorCameraOriginalPriority;
                _bSpectatorCameraActive = false;
            }
        }

        // ==================== 状态过滤 ====================

        /// <summary>
        /// 玩家状态变化时重建目标组，使出局玩家即时移出镜头取景
        /// </summary>
        private void HandlePlayerStateChanged(PlayerController player, PlayerStateType stateType)
        {
            RebuildTargets();
            UpdateSpectatorCamera();
        }

        /// <summary>
        /// 判断玩家是否应留在镜头目标组中
        /// 规则：
        /// - 存活玩家：始终入组
        /// - 冻结玩家：始终入组（被冰封但仍在场上，解冻后恢复存活，镜头保持取景）
        /// - 远端玩家：非存活（幽灵/通关）即移出
        /// - 本地玩家：幽灵状态保留（玩家仍在操控幽灵移动，镜头需跟随）
        ///             通关状态移出（已停止行为，无需再框选）
        /// </summary>
        protected virtual bool ShouldTrack(PlayerController player, PlayerStateType state)
        {
            if (state == PlayerStateType.Alive || state == PlayerStateType.Frozen)
            {
                return true;
            }

            if (player.BIsLocal && state == PlayerStateType.Ghost)
            {
                return true;
            }

            return false;
        }

        // ==================== 目标维护 ====================

        /// <summary>
        /// 按当前玩家集合全量重建 TargetGroup
        /// 玩家数量少（房间制），全量重建开销可忽略，且能自动清理已销毁的空引用
        /// </summary>
        public void RebuildTargets()
        {
            if (_targetGroup == null)
            {
                return;
            }

            _targetGroup.m_Targets = new CinemachineTargetGroup.Target[0];
            _addedTargets.Clear();

            foreach (Transform target in CollectTargets())
            {
                AddTarget(target);
            }

            UpdateSpectatorCamera();
        }

        // ==================== 通关观战镜头 ====================

        /// <summary>
        /// 本地玩家通关后，将主镜头平滑切换至固定观战镜头（Cinemachine Brain 按优先级差自动混合）；
        /// 本地玩家复活回到存活状态时还原优先级，镜头平滑回到游玩目标组。
        /// 解决"本地玩家通关后被移出目标组，PlayingCamera 无目标而生硬变大"的问题。
        /// 时序说明：新一轮复活发生在 PropSelectionPhase.OnEnter 开头，
        /// 先于选择/放置 Director 记录镜头原优先级，优先级还原不会与阶段镜头切换冲突。
        /// </summary>
        private void UpdateSpectatorCamera()
        {
            if (_finishedSpectatorCamera == null)
            {
                return;
            }

            if (AnyLocalPlayerFinished())
            {
                if (!_bSpectatorCameraActive)
                {
                    _spectatorCameraOriginalPriority = _finishedSpectatorCamera.Priority;
                    _finishedSpectatorCamera.Priority = _finishedSpectatorCameraPriority;
                    _bSpectatorCameraActive = true;
                }
            }
            else if (_bSpectatorCameraActive)
            {
                _finishedSpectatorCamera.Priority = _spectatorCameraOriginalPriority;
                _bSpectatorCameraActive = false;
            }
        }

        /// <summary>
        /// 是否存在处于通关状态的本地玩家
        /// </summary>
        private bool AnyLocalPlayerFinished()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return false;
            }

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player != null && player.BIsLocal
                    && registry.GetPlayerState(player) == PlayerStateType.Finished)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 收集应入组的目标 Transform
        /// 默认按 ShouldTrack 规则过滤（存活玩家 + 幽灵状态的本地玩家）
        /// 子类可重写 ShouldTrack 调整过滤规则，或重写本方法完全自定义目标来源
        /// </summary>
        protected virtual IEnumerable<Transform> CollectTargets()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                yield break;
            }

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player == null)
                {
                    continue;
                }

                if (ShouldTrack(player, registry.GetPlayerState(player)))
                {
                    yield return player.transform;
                }
            }
        }

        /// <summary>
        /// 将单个 Transform 加入 TargetGroup（自动去重）
        /// </summary>
        private void AddTarget(Transform target)
        {
            if (target == null || !_addedTargets.Add(target))
            {
                return;
            }

            var targets = new List<CinemachineTargetGroup.Target>(_targetGroup.m_Targets)
            {
                new CinemachineTargetGroup.Target
                {
                    target = target,
                    weight = _targetWeight,
                    radius = _targetRadius
                }
            };
            _targetGroup.m_Targets = targets.ToArray();
        }
    }
}
