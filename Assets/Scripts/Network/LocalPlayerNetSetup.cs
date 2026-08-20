using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Network
{
    /// <summary>
    /// 本地玩家网络接入（由 RoomSnapshotReceiver 自动携带，跨场景存活）。
    ///
    /// 大厅流程（Hall→Room→Level1）不经过 NetDebugBootstrap，本地玩家的
    /// 位置上报组件（InputReporter）与出局上报组件（PlayerOutReporter）在此自动挂载：
    ///   每帧检测"已进房 + 本地玩家已生成"，就绪后一次性挂载并自我停用。
    ///
    /// 与 NetDebugBootstrap 的挂载逻辑幂等（都判 HasComponent 再挂）。
    /// </summary>
    public class LocalPlayerNetSetup : MonoBehaviour
    {
        private bool _done;

        private void Update()
        {
            if (_done) return;

            NetworkManager net = NetworkManager.Instance;
            if (net == null || !net.IsConnected
                || string.IsNullOrEmpty(net.RoomId)
                || string.IsNullOrEmpty(net.LocalPlayerId)) return;

            PlayerController local = FindLocalPlayer();
            if (local == null) return;

            _done = true;

            // 写入网络身份（与 NetDebugBootstrap 一致：注册表/上报都以服务端 playerId 为准）
            PlayerProfile profile = local.BuildProfile();
            profile.PlayerId = net.LocalPlayerId;
            profile.IsLocal = true;
            local.ApplyProfile(profile);

            if (local.GetComponent<InputReporter>() == null)
            {
                local.gameObject.AddComponent<InputReporter>();
            }
            if (local.GetComponent<PlayerOutReporter>() == null)
            {
                local.gameObject.AddComponent<PlayerOutReporter>();
            }

            Debug.Log($"[NetWork] 本地玩家已接入联机（大厅流程）: playerId={net.LocalPlayerId} name={local.PlayerName}");
        }

        private static PlayerController FindLocalPlayer()
        {
            if (LevelPlayerRegistry.Instance == null) return null;

            System.Collections.Generic.IReadOnlyList<PlayerController> players = LevelPlayerRegistry.Instance.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].BIsLocal)
                {
                    return players[i];
                }
            }
            return null;
        }
    }
}
