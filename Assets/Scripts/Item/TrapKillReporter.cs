using SuperQQ.Player;
using SuperQQ.Score;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 陷阱击杀归属上报：击杀点（KillZone/MeteorHammerHead 等）在致死瞬间调用，
    /// 把"谁摆的陷阱杀了人"记入本轮计分（每次有效击杀 +5，单轮上限 2 次），联机时同步广播。
    ///
    /// 只在受害者本地端触发（联机下远端玩家碰撞体已关闭，不会在他端误触发）；
    /// 归属来自 PlacedItem.OwnerKey（本地确认/远端生成/快照恢复三条摆放路径写入）。
    /// 自杀（自己的陷阱杀自己）与关卡原生陷阱（无归属）不计分。
    /// </summary>
    public static class TrapKillReporter
    {
        /// <summary>
        /// 上报一次陷阱击杀。
        /// </summary>
        /// <param name="killSource">致死组件（自身或父级链上应能找到 ItemBase）</param>
        /// <param name="victim">被击杀的玩家</param>
        public static void ReportKill(Component killSource, PlayerController victim)
        {
            if (killSource == null || victim == null)
            {
                return;
            }

            ItemBase item = killSource.GetComponentInParent<ItemBase>();
            string ownerKey = item != null && item.Placed != null ? item.Placed.OwnerKey : null;
            if (string.IsNullOrEmpty(ownerKey))
            {
                return; // 关卡原生陷阱无归属
            }

            if (ownerKey == victim.IdentityKey)
            {
                return; // 自杀不计
            }

            RecordLocal(ownerKey);
            // 联机广播给其他端（含陷阱主所在端），离线为空操作
            Network.NetEventSync.ReportTrapKill(ownerKey);
        }

        /// <summary>
        /// 为归属者记入一次陷阱有效击杀（本地击杀与网络广播接收两条路径共用）。
        /// ownerKey 为身份主键（联机 playerId / 单机 PlayerName），计分记录以玩家名为键，此处转换。
        /// </summary>
        public static void RecordLocal(string ownerKey)
        {
            if (string.IsNullOrEmpty(ownerKey))
            {
                return;
            }

            string ownerName = ownerKey;
            if (PlayerSessionManager.Instance != null)
            {
                PlayerProfile profile = PlayerSessionManager.Instance.GetProfileByIdentity(ownerKey);
                if (profile != null && !string.IsNullOrEmpty(profile.PlayerName))
                {
                    ownerName = profile.PlayerName;
                }
            }

            if (PlayerScoreManager.Instance != null)
            {
                PlayerScoreManager.Instance.RecordTrapKill(ownerName);
            }
        }
    }
}
