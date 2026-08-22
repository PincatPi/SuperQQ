using System.Collections.Generic;
using SuperQQ.Player;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 言出法随事件修饰符 — ScriptableObject 资产
    /// 事件被选中后：在场景固定位置创建一个法阵，持续整场游玩阶段
    /// 法阵触发范围由法阵 Prefab 上的 Trigger 碰撞体定义：玩家（Player 标签）进入范围时，
    /// 吟唱提示 Text 框显示在法阵旁的固定位置（默认"请吟唱"）；范围内无存活玩家时提示隐藏
    /// 提示框为屏幕空间 UI，实例化到主 Canvas 下，位置 = 法阵位置 + 可配置偏移（随相机实时换算）
    /// 在 Project 窗口选中本资产时，Scene 视图会标注法阵位置与提示框偏移（均可拖拽调节）
    /// 所有策划参数均在本资产上配置；运行时状态（法阵实例、提示框实例）不序列化
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
        [Tooltip("吟唱提示 Text 框 Prefab（屏幕空间 UI，根节点需挂 ChantPrompt，运行时实例化到主 Canvas 下）；留空则无提示 UI")]
        [SerializeField] private ChantPrompt _chantPromptPrefab;

        [Tooltip("提示文字内容")]
        [SerializeField] private string _promptText = "请吟唱";

        [Tooltip("提示框相对法阵的世界坐标偏移（Scene 视图中可拖拽调节）")]
        [SerializeField] private Vector2 _promptOffset = new Vector2(0f, 1.5f);

        // ==================== 运行时状态（非序列化，Activate 初始化 / Deactivate 清空） ====================

        // 法阵实例，Deactivate 时销毁
        private MagicCircle _circleInstance;

        // 当前处于法阵触发范围内的玩家（由法阵进出事件维护）
        private readonly HashSet<PlayerController> _playersInside = new();

        // 吟唱提示实例（整场一个，Deactivate 时销毁）
        private ChantPrompt _promptInstance;

        // 提示框的宿主 Canvas（主 Canvas，Activate 时解析）
        private RectTransform _promptCanvasRect;

        // 世界坐标转屏幕坐标所用相机（Activate 时缓存）
        private Camera _camera;

        // ==================== LevelEventModifier 实现 ====================

        /// <summary>
        /// 激活事件：在固定位置创建法阵与吟唱提示（初始隐藏），订阅法阵进出事件与玩家状态事件
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

            CreatePrompt();

            if (LevelPlayerRegistry.Instance != null)
            {
                // 状态变化驱动提示的显示/隐藏（如范围内玩家全部死亡时隐藏）
                LevelPlayerRegistry.Instance.OnPlayerStateChanged += HandlePlayerStateChanged;
                // 玩家化身销毁时清理其残留记录
                LevelPlayerRegistry.Instance.OnPlayersChanged += HandlePlayersChanged;
            }
        }

        /// <summary>
        /// 停用事件：退订全部事件，销毁法阵与吟唱提示，清空运行时状态
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

            if (_promptInstance != null)
            {
                Destroy(_promptInstance.gameObject);
                _promptInstance = null;
            }

            _playersInside.Clear();
            _promptCanvasRect = null;
            _camera = null;
        }

        // ==================== 事件响应 ====================

        /// <summary>
        /// 玩家进入法阵范围：记录到场并刷新提示显隐
        /// </summary>
        private void HandlePlayerEntered(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            _playersInside.Add(player);
            RefreshPromptVisibility();
        }

        /// <summary>
        /// 玩家离开法阵范围：移除在场记录并刷新提示显隐
        /// </summary>
        private void HandlePlayerExited(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            _playersInside.Remove(player);
            RefreshPromptVisibility();
        }

        /// <summary>
        /// 玩家状态变化：刷新提示显隐（范围内玩家全部出局时隐藏，恢复存活时重新显示）
        /// </summary>
        private void HandlePlayerStateChanged(PlayerController player, PlayerStateType stateType)
        {
            RefreshPromptVisibility();
        }

        /// <summary>
        /// 玩家集合变化（注册/注销）：清理已离场玩家的残留记录并刷新提示显隐
        /// </summary>
        private void HandlePlayersChanged()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            _playersInside.RemoveWhere(player => !IsRegistered(registry, player));
            RefreshPromptVisibility();
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

        // ==================== 吟唱提示管理 ====================

        /// <summary>
        /// 创建吟唱提示实例：实例化到主 Canvas 下，跟随法阵固定位置，初始隐藏
        /// 未配置 Prefab 或未找到主 Canvas 时跳过（无提示 UI 降级）
        /// </summary>
        private void CreatePrompt()
        {
            if (_chantPromptPrefab == null)
            {
                return;
            }

            _promptCanvasRect = ResolvePromptCanvasRect();
            _camera = Camera.main;
            if (_promptCanvasRect == null)
            {
                Debug.LogWarning("[MagicCircleModifier] 未找到主 Canvas，法阵将无提示 UI。");
                return;
            }

            _promptInstance = Instantiate(_chantPromptPrefab, _promptCanvasRect, false);
            _promptInstance.Initialize(_camera, _promptCanvasRect, _circleInstance.transform, _promptOffset);
            _promptInstance.SetText(_promptText);
            _promptInstance.gameObject.SetActive(false);
        }

        /// <summary>
        /// 刷新提示显隐：法阵范围内存在存活（Alive）玩家时显示，否则隐藏
        /// </summary>
        private void RefreshPromptVisibility()
        {
            if (_promptInstance == null)
            {
                return;
            }

            _promptInstance.gameObject.SetActive(HasAlivePlayerInside());
        }

        /// <summary>
        /// 法阵范围内是否存在存活玩家
        /// </summary>
        private bool HasAlivePlayerInside()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return false;
            }

            foreach (PlayerController player in _playersInside)
            {
                if (player != null && registry.GetPlayerState(player) == PlayerStateType.Alive)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 解析提示框的宿主 Canvas：优先玩家名称标签管理器所在的 Canvas（玩家头顶 UI 的既有宿主），
        /// 其次 PopupManager 所在的 Canvas；均未找到时返回 null
        /// </summary>
        private static RectTransform ResolvePromptCanvasRect()
        {
            Canvas canvas = null;

            if (PlayerNameLabelManager.Instance != null)
            {
                canvas = PlayerNameLabelManager.Instance.GetComponentInParent<Canvas>();
            }

            if (canvas == null && PopupManager.Instance != null)
            {
                canvas = PopupManager.Instance.GetComponentInParent<Canvas>();
            }

            return canvas != null ? canvas.GetComponent<RectTransform>() : null;
        }
    }
}
