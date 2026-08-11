using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.UI
{
    /// <summary>
    /// 触屏输入面板：挂在触屏按钮面板的根节点上。
    /// 运行时判断平台：
    ///   移动端（或 Editor 勾选强制开关）→ 显示面板，并将本地玩家的输入源替换为 TouchPlayerInput；
    ///   PC → 隐藏整个面板，保持键盘输入。
    /// 只绑定 BIsLocal == true 的本地玩家，联机远程玩家不受影响。
    /// </summary>
    public class MobileInputPanel : MonoBehaviour
    {
        [Header("触屏按钮引用")]
        [SerializeField] private TouchInputButton leftButton;
        [SerializeField] private TouchInputButton rightButton;
        [SerializeField] private TouchInputButton jumpButton;
        [SerializeField] private TouchInputButton downButton;

        [Header("调试")]
        [SerializeField, Tooltip("在 PC/编辑器中也强制启用触屏输入，便于用鼠标模拟测试")]
        private bool forceTouchInEditor = false;

        private bool _bound;

        private bool TouchModeEnabled => Application.isMobilePlatform || forceTouchInEditor;

        private void Start()
        {
            if (!TouchModeEnabled)
            {
                // PC 端隐藏面板，零视觉干扰
                gameObject.SetActive(false);
                return;
            }
            TryBindLocalPlayer();
        }

        private void Update()
        {
            // 玩家化身可能晚于面板生成（联机流程），未绑定时轮询重试，绑定成功后停止
            if (!_bound)
            {
                TryBindLocalPlayer();
            }
        }

        private void TryBindLocalPlayer()
        {
            if (_bound) return;
            if (LevelPlayerRegistry.Instance == null) return;

            var players = LevelPlayerRegistry.Instance.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player == null || !player.BIsLocal) continue;

                player.SetInputSource(new TouchPlayerInput(leftButton, rightButton, jumpButton, downButton));
                _bound = true;
                Debug.Log($"[MobileInputPanel] 本地玩家 {player.PlayerName} 已切换为触屏输入");
                return;
            }
        }
    }
}
