using SuperQQ.GameFlow;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.UI
{
    /// <summary>
    /// 触屏输入面板：挂在触屏输入面板的根节点上。
    /// 运行时判断平台：
    ///   移动端（或 Editor 勾选强制开关）→ 显示面板，并将本地玩家的输入源替换为 JoystickPlayerInput；
    ///   PC → 隐藏整个面板，保持键盘输入。
    /// 移动由虚拟摇杆（VirtualJoystick）控制：存活状态仅左右 + 独立跳跃键，幽灵状态四向移动并隐藏跳跃键。
    /// 阶段显隐：仅 PlayingPhase（游玩阶段）显示按键，其余阶段经 CanvasGroup 隐藏（不用 SetActive，
    /// 保证隐藏期间脚本仍运行、能收到 GamePhaseManager.OnPhaseChanged 阶段事件）。
    /// 只绑定 BIsLocal == true 的本地玩家，联机远程玩家不受影响。
    /// 嘲讽按键（tauntButton，面板上的 DownBtn）：按下沿触发本地玩家 PlayerAnimationController.PlayTaunt，
    /// 打断逻辑由 Animator 过渡实现（移动/跳跃条件过渡 + Taunt 自过渡）
    /// </summary>
    public class MobileInputPanel : MonoBehaviour
    {
        [Header("触屏输入引用")]
        [SerializeField, Tooltip("移动摇杆（background 上挂 VirtualJoystick，center 为可拖动把手）")]
        private VirtualJoystick moveJoystick;
        [SerializeField, Tooltip("独立跳跃按钮（存活状态的唯一向上手段）")]
        private TouchInputButton jumpButton;
        [SerializeField, Tooltip("嘲讽按钮（面板上的 DownBtn，按下播放嘲讽表情）")]
        private TouchInputButton tauntButton;

        [Header("摇杆参数")]
        [SerializeField, Range(0.05f, 1f), Tooltip("摇杆轴向触发阈值：偏移比例达到该值视为按下对应方向键")]
        private float axisThreshold = 0.3f;

        [Header("调试")]
        [SerializeField, Tooltip("在 PC/编辑器中也强制启用触屏输入，便于用鼠标模拟测试")]
        private bool forceTouchInEditor = false;

        private JoystickPlayerInput _touchInput;
        private PlayerController _localPlayer;
        private PlayerAnimationController _tauntAnimCtrl;   // 本地玩家的动画驱动器（嘲讽目标）
        private bool _tauntButtonHeld;                      // 上帧嘲讽按键按压状态（按下沿检测用）
        private bool _jumpButtonVisible = true;             // 跳跃键当前显示状态（避免每帧重复 SetActive）
        private CanvasGroup _canvasGroup;                   // 阶段显隐控制（隐藏时仍接收阶段事件）
        private bool _bControlsVisible = true;              // 当前是否处于 PlayingPhase 显示状态

        private bool TouchModeEnabled => Application.isMobilePlatform || forceTouchInEditor;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void OnEnable()
        {
            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            }
        }

        private void OnDisable()
        {
            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
            }
        }

        private void Start()
        {
            if (!TouchModeEnabled)
            {
                // PC 端隐藏面板，零视觉干扰
                gameObject.SetActive(false);
                return;
            }
            _touchInput = new JoystickPlayerInput(moveJoystick, jumpButton, axisThreshold);
            TryBindLocalPlayer();

            // Awake/OnEnable 可能早于 Manager 单例就绪，Start 兜底补订阅并同步初始显隐
            if (GamePhaseManager.Instance != null)
            {
                GamePhaseManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
                GamePhaseManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            }
            SetControlsVisible(GamePhaseManager.Instance != null
                && GamePhaseManager.Instance.CurrentPhaseAsset is PlayingPhase);
        }

        private void Update()
        {
            // PC 端面板已在 Start 中自隐藏，不会进入 Update；此处兜底直接返回
            if (_touchInput == null) return;

            // 玩家化身可能晚于面板生成（联机流程），未找到时轮询重试
            if (_localPlayer == null)
            {
                TryBindLocalPlayer();
                return;
            }

            // 输入源可能被外部流程覆盖（如选择/放置阶段 PlayerAvatarGate 退出时还原了缓存的键盘输入），
            // 发现被覆盖时重新断言触屏输入；屏蔽期间（NullPlayerInput）不抢占，待其还原后再接管
            if (_localPlayer.InputSource != _touchInput && !(_localPlayer.InputSource is NullPlayerInput))
            {
                _localPlayer.SetInputSource(_touchInput);
                Debug.Log($"[MobileInputPanel] 本地玩家 {_localPlayer.PlayerName} 已切换为触屏输入");
            }

            // 按当前状态切换摇杆模式：幽灵四向移动，存活仅左右 + 独立跳跃键
            _touchInput.FourWayMode = _localPlayer.BIsGhost;

            UpdateJumpButtonVisibility();

            // 隐藏期间不读嘲讽按键（视觉上已不可见，避免残留按压误触发）
            if (_bControlsVisible)
            {
                UpdateTauntInput();
            }
        }

        /// <summary>
        /// 阶段切换回调：进入 PlayingPhase 显示触屏按键，离开即隐藏
        /// </summary>
        private void HandlePhaseChanged(GamePhaseBase previousPhase, GamePhaseBase nextPhase)
        {
            SetControlsVisible(nextPhase is PlayingPhase);
        }

        /// <summary>
        /// 经 CanvasGroup 切换面板显隐：不用 SetActive，保证隐藏期间脚本仍运行、能收到阶段事件。
        /// 隐藏时强制释放摇杆/跳跃/嘲讽按键的按压状态，避免阶段切换瞬间按住的手指造成输入残留
        /// </summary>
        private void SetControlsVisible(bool visible)
        {
            if (visible == _bControlsVisible) return;

            _bControlsVisible = visible;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = visible ? 1f : 0f;
                _canvasGroup.interactable = visible;
                _canvasGroup.blocksRaycasts = visible;
            }

            if (!visible)
            {
                if (moveJoystick != null) moveJoystick.ForceRelease();
                if (jumpButton != null) jumpButton.ForceRelease();
                if (tauntButton != null) tauntButton.ForceRelease();
                _tauntButtonHeld = false;
            }
        }

        /// <summary>
        /// 按本地玩家幽灵状态切换跳跃键显隐：
        /// 幽灵状态移动全由摇杆四向控制，隐藏跳跃键；恢复存活后重新显示。
        /// 隐藏时 TouchInputButton.OnDisable 会强制释放按压状态，不会残留卡键
        /// </summary>
        private void UpdateJumpButtonVisibility()
        {
            if (jumpButton == null) return;

            bool shouldShow = !_localPlayer.BIsGhost;
            if (shouldShow == _jumpButtonVisible) return;

            _jumpButtonVisible = shouldShow;
            jumpButton.gameObject.SetActive(shouldShow);
        }

        /// <summary>
        /// 读取嘲讽按键并触发本地玩家嘲讽动画：
        /// 按下沿触发（本帧按住且上帧未按住，与 JoystickPlayerInput 的 JumpPressed 语义一致），
        /// 按住不连发、松开后可再次触发；嘲讽播放中再次按下可重新触发（打断自身）
        /// </summary>
        private void UpdateTauntInput()
        {
            if (tauntButton == null) return;

            bool pressed = tauntButton.IsPressed;
            if (pressed && !_tauntButtonHeld && _tauntAnimCtrl != null)
            {
                _tauntAnimCtrl.PlayTaunt();
            }
            _tauntButtonHeld = pressed;
        }

        private void TryBindLocalPlayer()
        {
            if (_localPlayer != null) return;
            if (LevelPlayerRegistry.Instance == null) return;

            var players = LevelPlayerRegistry.Instance.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player == null || !player.BIsLocal) continue;

                _localPlayer = player;
                _tauntAnimCtrl = player.GetComponent<PlayerAnimationController>();
                player.SetInputSource(_touchInput);
                Debug.Log($"[MobileInputPanel] 本地玩家 {player.PlayerName} 已切换为触屏输入");
                return;
            }
        }
    }
}
