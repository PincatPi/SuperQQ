using SuperQQ.UI;

namespace SuperQQ.Player
{
    /// <summary>
    /// 触屏输入实现：从 4 个 TouchInputButton 读取按压状态，
    /// 合成与 LocalPlayerInput（键盘）语义完全一致的输入数据。
    /// JumpPressed 沿触发通过与上一帧 JumpHeld 对比实现，与 Update 节奏对齐。
    /// </summary>
    public class TouchPlayerInput : IPlayerInput
    {
        public float Horizontal { get; private set; }
        public float Vertical { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool JumpHeld { get; private set; }

        private readonly TouchInputButton leftButton;
        private readonly TouchInputButton rightButton;
        private readonly TouchInputButton jumpButton;
        private readonly TouchInputButton downButton;

        public TouchPlayerInput(TouchInputButton left, TouchInputButton right, TouchInputButton jump, TouchInputButton down)
        {
            leftButton = left;
            rightButton = right;
            jumpButton = jump;
            downButton = down;
        }

        public void ReadInput()
        {
            float h = 0f;
            float v = 0f;

            if (leftButton != null && leftButton.IsPressed) h -= 1f;
            if (rightButton != null && rightButton.IsPressed) h += 1f;

            bool jumpHeld = jumpButton != null && jumpButton.IsPressed;
            bool downHeld = downButton != null && downButton.IsPressed;

            if (jumpHeld) v += 1f;
            if (downHeld) v -= 1f;

            Horizontal = h;
            Vertical = v;
            // 沿触发：本帧按住且上帧未按住，等价于 Input.GetKeyDown
            JumpPressed = jumpHeld && !JumpHeld;
            JumpHeld = jumpHeld;
        }
    }
}
