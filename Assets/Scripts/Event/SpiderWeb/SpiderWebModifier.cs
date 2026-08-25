using System.Collections;
using System.Collections.Generic;
using SuperQQ.Player;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 蜘蛛网事件修饰符 — ScriptableObject 资产
    /// 事件被选中后：在场景中实例化蛛网群 Prefab（内含多处蛛网），持续整场游玩阶段
    /// 存活玩家触碰蛛网后全部运动（移动/跳跃/下落/飞行）同比例大幅降低
    /// （经 PlayerController 全运动减速乘区实现，类似《我的世界》蜘蛛网）；
    /// 本地玩家被困时主 Canvas 弹出挣脱进度条：双指（及以上）滑动屏幕累积进度
    /// （滑动距离越长累积越多、单指滑动不计入并给出提示、无有效滑动时进度持续衰减），
    /// 进度积满即挣脱成功，恢复移动速度；离开蛛网范围同样立即解除减速
    /// 所有策划参数均在本资产上配置；运行时状态（蛛网群实例、被困玩家、进度）不序列化
    /// </summary>
    [CreateAssetMenu(fileName = "SpiderWebModifier", menuName = "SuperQQ/Event/Spider Web Modifier")]
    public class SpiderWebModifier : LevelEventModifier
    {
        [Header("蛛网群")]
        [Tooltip("蛛网群 Prefab（内含多处蛛网，每个蛛网物体需挂 SpiderWeb 脚本与 Trigger 碰撞体），激活时整体实例化，停用时销毁；留空则事件不生效")]
        [SerializeField] private GameObject _webFieldPrefab;

        [Tooltip("蛛网位置偏移（世界坐标）：蛛网群 Prefab 实例化时的整体位移，默认原地")]
        [SerializeField] private Vector2 _webFieldPosition = Vector2.zero;

        [Header("减速")]
        [Tooltip("触碰蛛网后的移速降低比例（0~1）：如 0.9 表示降低 90%，玩家仅保留 10% 移速")]
        [Range(0f, 1f)]
        [SerializeField] private float _slowFactor = 0.9f;

        [Header("挣脱进度")]
        [Tooltip("挣脱进度条 UI Prefab（屏幕空间 UI，根节点需挂 WebStruggleBar）；留空则无进度条 UI")]
        [SerializeField] private WebStruggleBar _struggleBarPrefab;

        [Tooltip("填满挣脱进度条所需的双指累计滑动总距离（像素）：数值越小越容易挣脱")]
        [Min(100f)]
        [SerializeField] private float _swipePixelsToEscape = 3000f;

        [Tooltip("无有效双指滑动时进度每秒回退比例（0~1/秒）")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _decayRate = 0.08f;

        [Tooltip("参与挣脱判定的最少手指数量（强制双指及以上滑动才累积进度）")]
        [Min(2)]
        [SerializeField] private int _requiredFingerCount = 2;

        [Header("Editor 调试")]
        [Tooltip("Editor 环境下允许用鼠标拖拽模拟双指滑动（真机上无效）")]
        [SerializeField] private bool _editorSimulateWithMouse = true;

        // ==================== 运行时状态（非序列化，Activate 初始化 / Deactivate 清空） ====================

        // 蛛网群实例，Deactivate 时销毁
        private GameObject _webFieldInstance;

        // 蛛网群中的全部蛛网触发器（Activate 时收集并订阅事件）
        private readonly List<SpiderWeb> _webs = new();

        // 挣脱逻辑协程引用，Deactivate 时停止
        private Coroutine _struggleCoroutine;

        // 当前被减速的玩家集合（触网即加入，离网/挣脱/死亡/销毁即移除）
        private readonly HashSet<PlayerController> _slowedPlayers = new();

        // 当前被困的本地玩家集合（_slowedPlayers 的子集，驱动挣脱进度条）
        private readonly List<PlayerController> _trappedLocalPlayers = new();

        // 挣脱进度（0~1，设备级共享：同设备手指操作无法区分属于哪个本地玩家）
        private float _escapeProgress;

        // 挣脱进度条实例（首个本地玩家被困时创建，Deactivate 时销毁）
        private WebStruggleBar _struggleBarInstance;

        // 失效被困记录的待清理缓存（复用避免分配）
        private readonly List<PlayerController> _stalePlayers = new();

        // ==================== LevelEventModifier 实现 ====================

        /// <summary>
        /// 激活事件：实例化蛛网群并订阅各蛛网进出事件，启动挣脱逻辑协程
        /// </summary>
        public override void Activate(LevelEventContext context)
        {
            if (_webFieldPrefab == null)
            {
                Debug.LogWarning("[SpiderWebModifier] 蛛网群 Prefab 未配置，事件不生效。");
                return;
            }

            Transform parent = context != null ? context.SceneRoot : null;
            _webFieldInstance = Instantiate(_webFieldPrefab, _webFieldPosition, Quaternion.identity, parent);

            _webFieldInstance.GetComponentsInChildren(true, _webs);
            if (_webs.Count == 0)
            {
                Debug.LogWarning("[SpiderWebModifier] 蛛网群 Prefab 中未找到任何 SpiderWeb 组件，事件不生效。");
                return;
            }
            for (int i = 0; i < _webs.Count; i++)
            {
                _webs[i].OnPlayerEntered += HandlePlayerEnteredWeb;
                _webs[i].OnPlayerExited += HandlePlayerExitedWeb;
            }

            if (_struggleBarPrefab == null)
            {
                Debug.LogWarning("[SpiderWebModifier] 挣脱进度条 Prefab 未配置，被困时将无进度条 UI。");
            }

            if (LevelPlayerRegistry.Instance != null)
            {
                // 状态变化：被困玩家死亡/冻结/通关时解除减速
                LevelPlayerRegistry.Instance.OnPlayerStateChanged += HandlePlayerStateChanged;
                // 玩家化身销毁时清理残留记录
                LevelPlayerRegistry.Instance.OnPlayersChanged += HandlePlayersChanged;
            }

            if (context != null && context.CoroutineRunner != null)
            {
                _struggleCoroutine = context.CoroutineRunner.StartCoroutine(StruggleRoutine());
            }
        }

        /// <summary>
        /// 停用事件：退订全部事件、停止协程、解除所有减速、销毁进度条与蛛网群
        /// </summary>
        public override void Deactivate(LevelEventContext context)
        {
            if (_struggleCoroutine != null && context != null && context.CoroutineRunner != null)
            {
                context.CoroutineRunner.StopCoroutine(_struggleCoroutine);
                _struggleCoroutine = null;
            }

            for (int i = 0; i < _webs.Count; i++)
            {
                if (_webs[i] != null)
                {
                    _webs[i].OnPlayerEntered -= HandlePlayerEnteredWeb;
                    _webs[i].OnPlayerExited -= HandlePlayerExitedWeb;
                }
            }
            _webs.Clear();

            if (LevelPlayerRegistry.Instance != null)
            {
                LevelPlayerRegistry.Instance.OnPlayerStateChanged -= HandlePlayerStateChanged;
                LevelPlayerRegistry.Instance.OnPlayersChanged -= HandlePlayersChanged;
            }

            // 解除所有仍生效的减速（含被困本地玩家）
            foreach (PlayerController player in _slowedPlayers)
            {
                if (player != null)
                {
                    player.ResetMotionSlow();
                }
            }
            _slowedPlayers.Clear();
            _trappedLocalPlayers.Clear();

            if (_struggleBarInstance != null)
            {
                Destroy(_struggleBarInstance.gameObject);
                _struggleBarInstance = null;
            }

            if (_webFieldInstance != null)
            {
                // 场景正常销毁时蛛网群可能已随之销毁，此处判空后兜底销毁
                Destroy(_webFieldInstance);
                _webFieldInstance = null;
            }

            _escapeProgress = 0f;
            _stalePlayers.Clear();
        }

        // ==================== 蛛网进出 ====================

        /// <summary>
        /// 玩家进入蛛网：存活玩家减速；本地玩家额外加入被困集合（驱动挣脱进度条）
        /// </summary>
        private void HandlePlayerEnteredWeb(PlayerController player)
        {
            if (player == null || !CanBeTrapped(player))
            {
                return;
            }

            player.SetMotionSlow(1f - _slowFactor);
            _slowedPlayers.Add(player);

            if (player.BIsLocal && !_trappedLocalPlayers.Contains(player))
            {
                _trappedLocalPlayers.Add(player);
            }
        }

        /// <summary>
        /// 玩家离开蛛网：解除减速并移出被困集合（挣脱进度在无人被困时自动重置）
        /// </summary>
        private void HandlePlayerExitedWeb(PlayerController player)
        {
            ReleasePlayer(player);
        }

        /// <summary>
        /// 玩家状态变化：被困玩家离开存活状态时解除减速（死亡/冻结/通关均解除）
        /// </summary>
        private void HandlePlayerStateChanged(PlayerController player, PlayerStateType stateType)
        {
            if (stateType != PlayerStateType.Alive && _slowedPlayers.Contains(player))
            {
                ReleasePlayer(player);
            }
        }

        /// <summary>
        /// 玩家集合变化（注册/注销）：清理已离场玩家的残留减速记录
        /// </summary>
        private void HandlePlayersChanged()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;

            _stalePlayers.Clear();
            foreach (PlayerController player in _slowedPlayers)
            {
                if (!IsRegistered(registry, player))
                {
                    _stalePlayers.Add(player);
                }
            }
            for (int i = 0; i < _stalePlayers.Count; i++)
            {
                ReleasePlayer(_stalePlayers[i]);
            }
            _stalePlayers.Clear();
        }

        /// <summary>
        /// 解除玩家减速并移出被困集合
        /// </summary>
        private void ReleasePlayer(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            if (_slowedPlayers.Remove(player))
            {
                player.ResetMotionSlow();
            }
            _trappedLocalPlayers.Remove(player);
        }

        /// <summary>
        /// 判断玩家当前是否可被蛛网困住：仅存活玩家（幽灵/通关/死亡/冻结不受影响）
        /// </summary>
        private static bool CanBeTrapped(PlayerController player)
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            return registry != null && registry.GetPlayerState(player) == PlayerStateType.Alive;
        }

        /// <summary>
        /// 判断玩家是否仍注册在 Registry 中（未注销、未销毁）
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

        // ==================== 挣脱逻辑 ====================

        /// <summary>
        /// 挣脱逻辑主循环：有本地玩家被困时累积/衰减挣脱进度，积满即挣脱
        /// 无人被困时进度归零、进度条隐藏
        /// </summary>
        private IEnumerator StruggleRoutine()
        {
            while (true)
            {
                if (_trappedLocalPlayers.Count > 0)
                {
                    UpdateStruggle();
                }
                else
                {
                    // 无人被困：进度归零并隐藏进度条（下次被困从头开始）
                    _escapeProgress = 0f;
                    SetStruggleBarVisible(false);
                }
                yield return null;
            }
        }

        /// <summary>
        /// 更新一帧挣脱进度：双指滑动按距离累积，单指滑动仅提示不累积，无滑动则衰减
        /// </summary>
        private void UpdateStruggle()
        {
            SetStruggleBarVisible(true);

            float swipeDistance = SampleMultiFingerSwipeDistance(out bool bSingleFingerSwiped);
            if (swipeDistance > 0f)
            {
                _escapeProgress += swipeDistance / _swipePixelsToEscape;
            }
            else
            {
                _escapeProgress -= _decayRate * Time.deltaTime;
            }
            _escapeProgress = Mathf.Clamp01(_escapeProgress);

            if (_struggleBarInstance != null)
            {
                _struggleBarInstance.SetProgress(_escapeProgress);
                _struggleBarInstance.SetHintVisible(bSingleFingerSwiped && swipeDistance <= 0f);
            }

            if (_escapeProgress >= 1f)
            {
                EscapeAll();
            }
        }

        /// <summary>
        /// 采样本帧的双指滑动总距离：仅当触摸手指数达到要求时累积各手指滑动距离；
        /// 单指滑动不计入（bSingleFingerSwiped 输出供提示显示）；
        /// Editor 下可用鼠标拖拽模拟双指滑动
        /// </summary>
        private float SampleMultiFingerSwipeDistance(out bool bSingleFingerSwiped)
        {
            bSingleFingerSwiped = false;

            int touchCount = UnityEngine.Input.touchCount;
            if (touchCount >= _requiredFingerCount)
            {
                float totalDistance = 0f;
                for (int i = 0; i < touchCount; i++)
                {
                    Touch touch = UnityEngine.Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Moved)
                    {
                        totalDistance += touch.deltaPosition.magnitude;
                    }
                }
                return totalDistance;
            }

            if (touchCount == 1 && UnityEngine.Input.GetTouch(0).phase == TouchPhase.Moved)
            {
                bSingleFingerSwiped = true;
            }

#if UNITY_EDITOR
            // Editor 调试：鼠标拖拽模拟双指滑动
            if (_editorSimulateWithMouse && UnityEngine.Input.GetMouseButton(0))
            {
                var mouseDelta = new Vector2(UnityEngine.Input.GetAxis("Mouse X"), UnityEngine.Input.GetAxis("Mouse Y"));
                return mouseDelta.magnitude * 100f; // 鼠标位移换算为近似像素
            }
#endif
            return 0f;
        }

        /// <summary>
        /// 挣脱成功：解除所有被困玩家的减速，隐藏进度条，进度归零
        /// </summary>
        private void EscapeAll()
        {
            _stalePlayers.Clear();
            _stalePlayers.AddRange(_slowedPlayers);
            for (int i = 0; i < _stalePlayers.Count; i++)
            {
                ReleasePlayer(_stalePlayers[i]);
            }
            _stalePlayers.Clear();

            _escapeProgress = 0f;
            SetStruggleBarVisible(false);
            Debug.Log("[SpiderWebModifier] 挣脱成功：玩家已摆脱蜘蛛网减速。");
        }

        // ==================== 挣脱进度条 ====================

        /// <summary>
        /// 显示/隐藏挣脱进度条（首个本地玩家被困时实例化到主 Canvas 下）
        /// </summary>
        private void SetStruggleBarVisible(bool visible)
        {
            if (_struggleBarPrefab == null)
            {
                return;
            }

            if (visible && _struggleBarInstance == null)
            {
                RectTransform canvasRect = ResolveMainCanvasRect();
                if (canvasRect == null)
                {
                    Debug.LogWarning("[SpiderWebModifier] 未找到主 Canvas，无法弹出挣脱进度条。");
                    return;
                }
                _struggleBarInstance = Instantiate(_struggleBarPrefab, canvasRect, false);
            }

            if (_struggleBarInstance != null && _struggleBarInstance.gameObject.activeSelf != visible)
            {
                _struggleBarInstance.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 解析本地主 Canvas：优先玩家名称标签管理器所在的 Canvas（玩家头顶 UI 的既有宿主），
        /// 其次 PopupManager 所在的 Canvas；均未找到时返回 null
        /// </summary>
        private static RectTransform ResolveMainCanvasRect()
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
