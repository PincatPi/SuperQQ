using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.UI
{
    /// <summary>
    /// 触屏输入面板：挂在触屏输入面板的根节点上。
    /// 运行时判断平台：
    ///   移动端（或 Editor 勾选强制开关）→ 显示面板，并将本地玩家的输入源替换为 JoystickPlayerInput；
    ///   PC → 隐藏整个面板，保持键盘输入。
    /// 移动由虚拟摇杆（VirtualJoystick）控制：存活状态仅左右 + 独立跳跃键，幽灵状态四向移动。
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

        private bool TouchModeEnabled => Application.isMobilePlatform || forceTouchInEditor;

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

            UpdateTauntInput();
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
