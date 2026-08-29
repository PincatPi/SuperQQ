using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 摇杆输入实现：从 VirtualJoystick 读取方向、从独立跳跃按钮读取按压，
    /// 合成与 LocalPlayerInput（键盘）语义完全一致的数字输入（-1/0/1）。
    /// 非四向模式（存活状态）：仅输出水平方向，竖直恒为 0（跳跃由独立按钮触发）；
    /// 四向模式（幽灵状态）：上下左右全方向输出。
    /// JumpPressed 沿触发通过与上一帧 JumpHeld 对比实现，与 Update 节奏对齐。
    /// </summary>
    public class JoystickPlayerInput : IPlayerInput
    {
        public float Horizontal { get; private set; }
        public float Vertical { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool JumpHeld { get; private set; }

        /// <summary>
        /// 四向移动模式（幽灵状态）开关：true 时竖直方向由摇杆上下输出；
        /// false 时竖直恒为 0，仅水平移动 + 独立跳跃键。由 MobileInputPanel 按本地玩家状态每帧同步
        /// </summary>
        public bool FourWayMode;

        private readonly VirtualJoystick joystick;
        private readonly TouchInputButton jumpButton;
        private readonly float axisThreshold;

        /// <param name="moveJoystick">移动摇杆</param>
        /// <param name="jump">独立跳跃按钮</param>
        /// <param name="threshold">轴向触发阈值：偏移比例达到该值视为按下对应方向键</param>
        public JoystickPlayerInput(VirtualJoystick moveJoystick, TouchInputButton jump, float threshold = 0.3f)
        {
            joystick = moveJoystick;
            jumpButton = jump;
            axisThreshold = Mathf.Clamp01(threshold);
        }

        public void ReadInput()
        {
            Vector2 dir = joystick != null ? joystick.Direction : Vector2.zero;

            Horizontal = DigitalAxis(dir.x);
            Vertical = FourWayMode ? DigitalAxis(dir.y) : 0f;

            bool jumpHeld = jumpButton != null && jumpButton.IsPressed;
            // 沿触发：本帧按住且上帧未按住，等价于 Input.GetKeyDown
            JumpPressed = jumpHeld && !JumpHeld;
            JumpHeld = jumpHeld;
        }

        /// <summary>将模拟偏移量化为键盘等效的数字输入（-1/0/1）</summary>
        private float DigitalAxis(float value)
        {
            if (value >= axisThreshold) return 1f;
            if (value <= -axisThreshold) return -1f;
            return 0f;
        }
    }
}
