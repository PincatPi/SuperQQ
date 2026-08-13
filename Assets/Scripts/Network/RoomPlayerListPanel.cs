using Minigame.Room.V1;
using UnityEngine;

namespace SuperQQ.Network
{
    /// <summary>
    /// 对局内房间玩家列表面板：显示当前房间所有玩家ID（自己标注"我"）。
    /// 数据来自 RoomSnapshotReceiver 缓存的最新快照（服务器房间表，最权威）。
    /// 用 OnGUI 实现，挂在物体上即用，无需场景里预搭 UI。
    /// </summary>
    public class RoomPlayerListPanel : MonoBehaviour
    {
        [Header("刷新间隔（秒）")]
        [SerializeField] private float refreshInterval = 0.5f;

        private RoomSnapshotReceiver _receiver;
        private float _timer;
        private string _display = "";
        private GUIStyle _style;
        private GUIStyle _titleStyle;

        private void Awake()
        {
            _receiver = GetComponent<RoomSnapshotReceiver>();
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < refreshInterval) return;
            _timer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null || string.IsNullOrEmpty(net.RoomId))
            {
                _display = "";
                return;
            }

            var sb = new System.Text.StringBuilder();
            RoomSnapshot snapshot = _receiver != null ? _receiver.LatestSnapshot : null;

            if (snapshot != null && snapshot.Players.Count > 0)
            {
                foreach (RoomPlayerState p in snapshot.Players)
                {
                    string playerId = p.Player?.PlayerId;
                    if (string.IsNullOrEmpty(playerId)) continue;

                    bool isMe = playerId == net.LocalPlayerId;
                    sb.Append(isMe ? "▶ " : "  ").Append(playerId);
                    if (isMe) sb.Append("（我）");
                    if (!p.Connected) sb.Append(" [离线]");
                    sb.Append('\n');
                }
            }
            else
            {
                // 快照未到时至少显示自己
                if (!string.IsNullOrEmpty(net.LocalPlayerId))
                {
                    sb.Append("▶ ").Append(net.LocalPlayerId).Append("（我）\n");
                }
            }

            _display = sb.ToString();
        }

        private void OnGUI()
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null || string.IsNullOrEmpty(net.RoomId)) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.026f),
                    normal = { textColor = Color.white }
                };
                _titleStyle = new GUIStyle(_style)
                {
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(1f, 0.9f, 0.4f) }
                };
            }

            float w = Screen.width * 0.28f;
            float lineH = _style.fontSize * 1.4f;
            int lines = 2 + (_display.Length > 0 ? _display.Split('\n').Length : 0);
            var rect = new Rect(10, 10, w, lineH * lines + 10);

            // 半透明底
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(rect);
            GUILayout.Label($"房间 {net.RoomId}", _titleStyle);
            GUILayout.Label(_display.TrimEnd('\n'), _style);
            GUILayout.EndArea();
        }
    }
}
