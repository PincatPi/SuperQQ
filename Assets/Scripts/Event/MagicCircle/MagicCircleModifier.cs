using System.Collections.Generic;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 言出法随事件修饰符 — ScriptableObject 资产
    /// 事件被选中后：在场景固定位置创建一个法阵，持续整场游玩阶段
    /// 法阵触发范围由法阵 Prefab 上的 Trigger 碰撞体定义：玩家（Player 标签）进入范围时，
    /// 其头顶弹出吟唱提示 Text 框（默认"请吟唱"）；走出范围后提示消失，重新走入会再次弹出
    /// 仅存活（Alive）玩家显示提示：玩家在范围内死亡/被冻结/通关时提示即时隐藏，恢复存活后重新弹出
    /// 提示框实例化为玩家子节点随玩家移动，无需每帧同步位置
    /// 所有策划参数均在本资产上配置；运行时状态（法阵实例、提示框记录）不序列化
    /// </summary>
    [CreateAssetMenu(fileName = "MagicCircleModifier", menuName = "SuperQQ/Event/Magic Circle Modifier")]
    public class MagicCircleModifier : LevelEventModifier
    {
        [Header("法阵")]
        [Tooltip("法阵 Prefab（根节点需挂 MagicCircle 脚本与 Trigger 碰撞体，碰撞体形状即触发范围），激活时在固定位置实例化")]
        [SerializeField] private MagicCircle _circlePrefab;

        [Tooltip("法阵在场景中的固定位置（世界坐标）")]
        [SerializeField] private Vector2 _circlePosition = Vector2.zero;

        [Header("吟唱提示")]
        [Tooltip("吟唱提示 Text 框 Prefab（根节点需挂 ChantPrompt，世界空间 UI 或 3D 文本均可）；留空则无提示 UI")]
        [SerializeField] private ChantPrompt _chantPromptPrefab;

        [Tooltip("提示文字内容")]
        [SerializeField] private string _promptText = "请吟唱";

        [Tooltip("提示框相对玩家的挂接偏移（头顶位置）")]
        [SerializeField] private Vector2 _promptOffset = new Vector2(0f, 1.2f);

        // ==================== 运行时状态（非序列化，Activate 初始化 / Deactivate 清空） ====================

        // 法阵实例，Deactivate 时销毁
        private MagicCircle _circleInstance;

        // 当前处于法阵触发范围内的玩家（由法阵进出事件维护）
        private readonly HashSet<PlayerController> _playersInside = new();

        // 当前弹出着吟唱提示的玩家 → 其实例
        private readonly Dictionary<PlayerController, ChantPrompt> _activePrompts = new();

        // ==================== LevelEventModifier 实现 ====================

        /// <summary>
        /// 激活事件：在固定位置创建法阵，订阅法阵进出事件与玩家状态事件
        /// </summary>
        public override void Activate(LevelEventContext context)
        {
            if (_circlePrefab == null)
            {
                Debug.LogWarning("[MagicCircleModifier] 法阵 Prefab 未配置，事件不生效。");
                return;
            }

            if (_chantPromptPrefab == null)
            {
                Debug.LogWarning("[MagicCircleModifier] 吟唱提示 Prefab 未配置，法阵将无提示 UI。");
            }

            Transform parent = context != null ? context.SceneRoot : null;
            _circleInstance = Instantiate(_circlePrefab, _circlePosition, Quaternion.identity, parent);
            _circleInstance.OnPlayerEntered += HandlePlayerEntered;
            _circleInstance.OnPlayerExited += HandlePlayerExited;

            if (LevelPlayerRegistry.Instance != null)
            {
                // 状态变化驱动提示的显示/隐藏（如范围内死亡即时隐藏、解冻恢复重新弹出）
                LevelPlayerRegistry.Instance.OnPlayerStateChanged += HandlePlayerStateChanged;
                // 玩家化身销毁时清理其残留记录
                LevelPlayerRegistry.Instance.OnPlayersChanged += HandlePlayersChanged;
            }
        }

        /// <summary>
        /// 停用事件：退订全部事件，销毁法阵与所有吟唱提示，清空运行时状态
        /// </summary>
        public override void Deactivate(LevelEventContext context)
        {
            if (_circleInstance != null)
            {
                _circleInstance.OnPlayerEntered -= HandlePlayerEntered;
                _circleInstance.OnPlayerExited -= HandlePlayerExited;
                // 场景正常销毁时法阵可能已随之销毁，此处判空后兜底销毁
                Destroy(_circleInstance.gameObject);
                _circleInstance = null;
            }

            if (LevelPlayerRegistry.Instance != null)
            {
                LevelPlayerRegistry.Instance.OnPlayerStateChanged -= HandlePlayerStateChanged;
                LevelPlayerRegistry.Instance.OnPlayersChanged -= HandlePlayersChanged;
            }

            foreach (KeyValuePair<PlayerController, ChantPrompt> pair in _activePrompts)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
            }
            _activePrompts.Clear();
            _playersInside.Clear();
        }

        // ==================== 事件响应 ====================

        /// <summary>
        /// 玩家进入法阵范围：记录到场并刷新其提示
        /// </summary>
        private void HandlePlayerEntered(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            _playersInside.Add(player);
            RefreshPrompt(player);
        }

        /// <summary>
        /// 玩家离开法阵范围：移除在场记录并刷新其提示
        /// </summary>
        private void HandlePlayerExited(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            _playersInside.Remove(player);
            RefreshPrompt(player);
        }

        /// <summary>
        /// 玩家状态变化：刷新其提示（范围内死亡/冻结即时隐藏，恢复存活重新弹出）
        /// </summary>
        private void HandlePlayerStateChanged(PlayerController player, PlayerStateType stateType)
        {
            RefreshPrompt(player);
        }

        /// <summary>
        /// 玩家集合变化（注册/注销）：清理已离场玩家的残留记录与提示
        /// </summary>
        private void HandlePlayersChanged()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;

            _playersInside.RemoveWhere(player => !IsRegistered(registry, player));

            _stalePromptPlayers.Clear();
            foreach (KeyValuePair<PlayerController, ChantPrompt> pair in _activePrompts)
            {
                if (!IsRegistered(registry, pair.Key))
                {
                    _stalePromptPlayers.Add(pair.Key);
                }
            }

            for (int i = 0; i < _stalePromptPlayers.Count; i++)
            {
                HidePrompt(_stalePromptPlayers[i]);
            }
        }

        /// <summary>
        /// 判断玩家是否仍注册在 Registry 中（未注销、未销毁）
        /// 房间制玩家数量少，线性查找开销可忽略
        /// </summary>
        private static bool IsRegistered(LevelPlayerRegistry registry, PlayerController player)
        {
            if (registry == null || player == null)
            {
                return false;
            }

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == player)
                {
                    return true;
                }
            }
            return false;
        }

        // 失效提示记录的玩家缓存（复用避免每次分配）
        private readonly List<PlayerController> _stalePromptPlayers = new();

        // ==================== 吟唱提示管理 ====================

        /// <summary>
        /// 按"在法阵范围内且为存活状态"的口径刷新指定玩家的提示显示
        /// </summary>
        private void RefreshPrompt(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            bool bShouldShow = _playersInside.Contains(player)
                && registry != null
                && registry.GetPlayerState(player) == PlayerStateType.Alive;

            bool bIsShown = _activePrompts.ContainsKey(player);
            if (bShouldShow && !bIsShown)
            {
                ShowPrompt(player);
            }
            else if (!bShouldShow && bIsShown)
            {
                HidePrompt(player);
            }
        }

        /// <summary>
        /// 在玩家头顶弹出吟唱提示：实例化为玩家子节点随其移动，并设置提示文字
        /// </summary>
        private void ShowPrompt(PlayerController player)
        {
            if (_chantPromptPrefab == null)
            {
                return;
            }

            ChantPrompt prompt = Instantiate(_chantPromptPrefab, player.transform);
            prompt.transform.localPosition = _promptOffset;
            prompt.SetText(_promptText);
            _activePrompts[player] = prompt;
        }

        /// <summary>
        /// 移除指定玩家的吟唱提示
        /// </summary>
        private void HidePrompt(PlayerController player)
        {
            if (_activePrompts.TryGetValue(player, out ChantPrompt prompt))
            {
                if (prompt != null)
                {
                    Destroy(prompt.gameObject);
                }
                _activePrompts.Remove(player);
            }
        }
    }
}
