using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 旋转吐司尺寸同步：每轮开始时决定本轮尺寸（1/2/3），所有玩家一致。
    ///
    /// 两条路径：
    /// 1. 联机：房主端 DecideSizeLocally() 本地随机 → OnUploadSize 钩子发给服务器
    ///    （网络消息接好后在此挂钩，proto/服务器就绪前不影响单机流程）；
    ///    远端收到广播后 ApplySyncedSize() 应用，保证各端一致。
    /// 2. 确定性种子：SetRoundSeed() 后用同一种子 RollSize()，各端结果天然一致（推荐，
    ///    无需额外网络消息，种子可随房间/轮次信息下发）。
    ///
    /// 已决定的尺寸存于 CurrentSize：新实例化的 RotatingToast 在 Awake 自动应用；
    /// 场上已存在的实例通过 ApplySyncedSize 同步更新。
    /// </summary>
    public static class RotatingToastSizeSync
    {
        /// <summary>本轮尺寸（1/2/3；0=尚未决定）</summary>
        public static int CurrentSize { get; private set; }

        /// <summary>
        /// 上传钩子：房主端本地随机完成后调用，参数为本轮尺寸。
        /// 联机消息（proto/服务器）就绪后在此挂上报逻辑；未挂钩时仅本地生效。
        /// </summary>
        public static Action<int> OnUploadSize;

        // 场上实例登记表：尺寸变更时同步更新
        private static readonly List<RotatingToast> _instances = new();

        /// <summary>登记场上实例（RotatingToast 放置/实例化时调用）</summary>
        public static void Register(RotatingToast toast)
        {
            if (toast != null && !_instances.Contains(toast))
            {
                _instances.Add(toast);
            }
        }

        /// <summary>注销（道具移除时调用）</summary>
        public static void Unregister(RotatingToast toast)
        {
            _instances.Remove(toast);
        }

        // ==================== 尺寸决定 ====================

        /// <summary>
        /// 房主端：本地随机决定本轮尺寸并上传服务器（同步给所有玩家）
        /// </summary>
        /// <returns>决定的尺寸（1/2/3）</returns>
        public static int DecideSizeLocally()
        {
            int size = UnityEngine.Random.Range(1, 4);
            ApplySyncedSize(size);
            OnUploadSize?.Invoke(size);
            return size;
        }

        /// <summary>
        /// 确定性随机：同一种子各端结果一致（推荐联机方案，无需额外消息）
        /// </summary>
        public static int RollSize(int roundSeed)
        {
            var rng = new System.Random(roundSeed);
            return rng.Next(1, 4);
        }

        /// <summary>
        /// 用轮次种子决定并应用本轮尺寸（种子由房间/轮次信息提供，各端一致）
        /// </summary>
        public static int DecideSizeBySeed(int roundSeed)
        {
            int size = RollSize(roundSeed);
            ApplySyncedSize(size);
            return size;
        }

        // ==================== 尺寸应用 ====================

        /// <summary>
        /// 应用同步后的尺寸（远端广播到达 / 种子计算完成后调用）：
        /// 更新 CurrentSize，并同步场上所有已存在的旋转吐司实例；
        /// 之后实例化的实例在 Awake 自动读取 CurrentSize
        /// </summary>
        public static void ApplySyncedSize(int size)
        {
            CurrentSize = Mathf.Clamp(size, 1, 3);
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                if (_instances[i] == null)
                {
                    _instances.RemoveAt(i);
                    continue;
                }
                _instances[i].SetSize(CurrentSize);
            }
        }

        /// <summary>新一轮/退出房间时清空</summary>
        public static void ClearAll()
        {
            CurrentSize = 0;
            _instances.Clear();
        }
    }
}
