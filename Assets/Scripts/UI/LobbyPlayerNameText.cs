using SuperQQ.Network;
using TMPro;
using UnityEngine;

namespace SuperQQ.UI
{
    /// <summary>
    /// 大厅玩家名称文本 — 挂在 Lobby 场景的 PlayerName Text 上
    /// 绑定本地玩家账号昵称（NetworkManager.LocalNickname，登录成功后写入）。
    /// 昵称可能晚于场景加载就绪（如重连登录进行中），未解析到时每帧重试，成功后停止轮询；
    /// 未联网/无昵称时显示可配置的兜底文本。
    /// </summary>
    public class LobbyPlayerNameText : MonoBehaviour
    {
        [Tooltip("显示玩家昵称的 TMP 文本；留空时取本物体上的 TMP_Text")]
        [SerializeField] private TMP_Text _nameText;

        [Tooltip("无账号昵称（未联网/未登录）时显示的兜底文本")]
        [SerializeField] private string _fallbackName = "Player";

        private bool _resolved;     // 昵称是否已解析显示（未成功时每帧重试）

        private void Awake()
        {
            if (_nameText == null)
            {
                _nameText = GetComponent<TMP_Text>();
            }

            // 先显示兜底文本，昵称就绪后覆盖
            if (_nameText != null)
            {
                _nameText.text = _fallbackName;
            }
        }

        private void Update()
        {
            if (_resolved)
            {
                return;
            }

            NetworkManager net = NetworkManager.Instance;
            if (net == null || string.IsNullOrEmpty(net.LocalNickname))
            {
                return;
            }

            if (_nameText != null)
            {
                _nameText.text = net.LocalNickname;
            }
            _resolved = true;
        }
    }
}
