using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 玩家输入抽象接口。
    /// 本地玩家由 LocalPlayerInput 从键盘读取；
    /// 联机模式下远程玩家由 RemotePlayerInput（待实现）从网络快照提供。
    /// PlayerController 状态机只依赖此接口，不关心输入来源。
    /// </summary>
    public interface IPlayerInput
    {
        /// <summary>水平输入：-1 左 / 0 无 / 1 右</summary>
        float Horizontal { get; }

        /// <summary>垂直输入：-1 下 / 0 无 / 1 上（跳跃方向）</summary>
        float Vertical { get; }

        /// <summary>本帧是否按下跳跃（沿触发，仅一帧为 true）</summary>
        bool JumpPressed { get; }

        /// <summary>跳跃键是否持续按住（用于长按跳高）</summary>
        bool JumpHeld { get; }

        /// <summary>本帧是否按下嘲讽（沿触发，仅一帧为 true）</summary>
        bool TauntPressed { get; }

        /// <summary>每帧刷新输入状态，由 PlayerController.Update 调用</summary>
        void ReadInput();
    }

    /// <summary>
    /// 本地键盘输入实现。
    /// 键位来自 PlayerProfile（ApplyProfile 时通过 SetKeys 更新），
    /// 读取逻辑与联机改造前的 PlayerController.ReadInput 完全一致。
    /// </summary>
    public class LocalPlayerInput : IPlayerInput
    {
        public float Horizontal { get; private set; }
        public float Vertical { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool JumpHeld { get; private set; }
        public bool TauntPressed { get; private set; }

        private KeyCode leftKey;
        private KeyCode rightKey;
        private KeyCode jumpKey;
        private KeyCode jumpKeyAlt;
        private KeyCode downKey;
        private KeyCode tauntKey;

        public LocalPlayerInput(KeyCode left, KeyCode right, KeyCode jump, KeyCode jumpAlt, KeyCode down, KeyCode taunt)
        {
            SetKeys(left, right, jump, jumpAlt, down, taunt);
        }

        /// <summary>更新键位配置（应用玩家档案时调用）</summary>
        public void SetKeys(KeyCode left, KeyCode right, KeyCode jump, KeyCode jumpAlt, KeyCode down, KeyCode taunt)
        {
            leftKey = left;
            rightKey = right;
            jumpKey = jump;
            jumpKeyAlt = jumpAlt;
            downKey = down;
            tauntKey = taunt;
        }

        public void ReadInput()
        {
            float h = 0f;
            float v = 0f;

            if (Input.GetKey(leftKey)) h -= 1f;
            if (Input.GetKey(rightKey)) h += 1f;
            if (Input.GetKey(jumpKey) || Input.GetKey(jumpKeyAlt)) v += 1f;
            if (Input.GetKey(downKey)) v -= 1f;

            Horizontal = h;
            Vertical = v;
            JumpPressed = Input.GetKeyDown(jumpKey) || Input.GetKeyDown(jumpKeyAlt);
            JumpHeld = Input.GetKey(jumpKey) || Input.GetKey(jumpKeyAlt);
            TauntPressed = Input.GetKeyDown(tauntKey);
        }
    }

    /// <summary>
    /// 空输入实现：读数恒为静止。
    /// 需要临时屏蔽角色移动操作的场景（如道具放置阶段）注入本实例即可，
    /// 角色状态机照常运行但不会收到任何操作输入。
    /// </summary>
    public class NullPlayerInput : IPlayerInput
    {
        /// <summary>全局共享实例（无内部状态，可安全复用）</summary>
        public static readonly NullPlayerInput Instance = new NullPlayerInput();

        public float Horizontal => 0f;
        public float Vertical => 0f;
        public bool JumpPressed => false;
        public bool JumpHeld => false;
        public bool TauntPressed => false;

        public void ReadInput()
        {
        }
    }
}
