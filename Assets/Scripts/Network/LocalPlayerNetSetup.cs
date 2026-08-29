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
            if (EnsureLocalIdentityNow())
            {
                _done = true;
            }
        }

        /// <summary>
        /// 立即为本地玩家写入网络身份（playerId）并挂载上报组件（幂等）。
        /// 返回 true 表示已完成；未连接/未进房/本地玩家未生成时返回 false，等待重试。
        /// 除 Update 轮询外，选择阶段入口（PropSelectionDirector.BeginPhase）也会调用：
        /// 服务器阶段消息可能先于本组件的 Update 到达，若 playerId 未写入，
        /// 本地玩家 IdentityKey 会回退为玩家名，导致选择面板图标主键错误并在身份写入后重复生成。
        /// </summary>
        public static bool EnsureLocalIdentityNow()
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null || !net.IsConnected
                || string.IsNullOrEmpty(net.RoomId)
                || string.IsNullOrEmpty(net.LocalPlayerId)) return false;

            PlayerController local = FindLocalPlayer();
            if (local == null) return false;

            // 写入网络身份（与 NetDebugBootstrap 一致：注册表/上报都以服务端 playerId 为准）
            if (local.PlayerId != net.LocalPlayerId || !local.BIsLocal)
            {
                PlayerProfile profile = local.BuildProfile();
                profile.PlayerId = net.LocalPlayerId;
                profile.IsLocal = true;
                local.ApplyProfile(profile);
                Debug.Log($"[NetWork] 本地玩家已接入联机（大厅流程）: playerId={net.LocalPlayerId} name={local.PlayerName}");
            }

            // 同步写入会话档案：结算面板按 playerId 查询服务器分数依赖档案身份。
            // 场景预置玩家的档案按局内名注册、PlayerId 为空，而 EnsureRemotePlayersReady 的合并
            // 按服务器昵称匹配（昵称≠局内名时合并不上），必须在这里兜底写入。
            PlayerSessionManager session = PlayerSessionManager.Instance;
            if (session != null)
            {
                PlayerProfile sessionProfile = session.GetProfile(local.PlayerName);
                if (sessionProfile == null)
                {
                    // 按名找不到时兜底取第一个无 PlayerId 的本地档案（改名等边缘场景）
                    System.Collections.Generic.IReadOnlyList<PlayerProfile> all = session.Profiles;
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i] != null && all[i].IsLocal && string.IsNullOrEmpty(all[i].PlayerId))
                        {
                            sessionProfile = all[i];
                            break;
                        }
                    }
                }
                if (sessionProfile != null && string.IsNullOrEmpty(sessionProfile.PlayerId))
                {
                    sessionProfile.PlayerId = net.LocalPlayerId;
                }
            }

            if (local.GetComponent<InputReporter>() == null)
            {
                local.gameObject.AddComponent<InputReporter>();
            }
            if (local.GetComponent<PlayerOutReporter>() == null)
            {
                local.gameObject.AddComponent<PlayerOutReporter>();
            }

            return true;
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
