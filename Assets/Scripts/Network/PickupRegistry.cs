using System.Collections.Generic;
using SuperQQ.Item;
using UnityEngine;

namespace SuperQQ.Network
{
    /// <summary>
    /// 收集物注册表：给场上每个收集物（金币等）分配稳定 pickup_id，
    /// 拾取经服务器裁决广播后，各端按 id 移除对应收集物。
    ///
    /// pickup_id 约定："coin_{anchorX}_{anchorY}"（以摆放锚点格子定位，各端一致）。
    /// 注册：Coin.OnPlaced 后自动注册（PickupId 属性）；
    /// 销毁：服务器 PickupClaimBroadcast 到达时按 id 找到实例销毁。
    /// </summary>
    public static class PickupRegistry
    {
        private static readonly Dictionary<string, Coin> _coins = new();

        /// <summary>由锚点格子生成金币的 pickup_id</summary>
        public static string MakeCoinId(Vector2Int anchorCell) => $"coin_{anchorCell.x}_{anchorCell.y}";

        /// <summary>注册一枚金币（确认摆放后调用）</summary>
        public static void Register(Coin coin)
        {
            if (coin?.Placed == null) return;
            _coins[MakeCoinId(coin.Placed.AnchorCell)] = coin;
        }

        /// <summary>注销（金币自身销毁路径调用，幂等）</summary>
        public static void Unregister(Coin coin)
        {
            if (coin?.Placed == null) return;
            string id = MakeCoinId(coin.Placed.AnchorCell);
            if (_coins.TryGetValue(id, out Coin existing) && existing == coin)
            {
                _coins.Remove(id);
            }
        }

        /// <summary>查询一枚金币是否已被认领（本地拾取前预检，避免重复触发）</summary>
        public static bool BIsClaimed(Vector2Int anchorCell) => _claimed.Contains(MakeCoinId(anchorCell));

        private static readonly HashSet<string> _claimed = new();

        /// <summary>服务器拾取裁决到达：标记认领并销毁对应收集物（各端一致移除）</summary>
        public static void MarkClaimed(string pickupId, string claimerPlayerId, bool isMine)
        {
            _claimed.Add(pickupId);

            if (_coins.TryGetValue(pickupId, out Coin coin) && coin != null)
            {
                // 自己拾取的端：Coin 已走 Collect 跟随流程（collected=true），不重复处理；
                // 其他端：金币转为跟随认领者的远端化身（表现同步）
                if (!isMine)
                {
                    coin.RemoveByRemoteClaim(claimerPlayerId);
                }
            }
        }

        /// <summary>新一轮/退出房间时清空（场景切换时由 NetEventSync 或关卡驱动调用）</summary>
        public static void ClearAll()
        {
            _coins.Clear();
            _claimed.Clear();
        }
    }
}
