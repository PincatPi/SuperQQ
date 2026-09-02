using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 旋转吐司尺寸同步：每轮开始时决定本轮尺寸，所有玩家一致。
    ///
    /// 【当前版本】尺寸固定为 FixedSize（3x3），客户端写死：忽略服务器轮次种子与
    /// 尺寸广播，各端天然一致。历史随机逻辑保留在 DecideSizeLocally/RollSize 注释中，
    /// 恢复随机时改回实现即可。
    ///
    /// 已决定的尺寸存于 CurrentSize：新实例化的 RotatingToast 在 Awake 自动应用；
    /// 场上已存在的实例通过 ApplySyncedSize 同步更新。
    /// </summary>
    public static class RotatingToastSizeSync
    {
        /// <summary>固定尺寸（格）：吐司固定 3x3，不再随机</summary>
        public const int FixedSize = 3;

        /// <summary>本轮尺寸（固定为 FixedSize；初始即为 FixedSize，不存在"未决定"状态）</summary>
        public static int CurrentSize { get; private set; } = FixedSize;

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
        /// 决定本轮尺寸并上传服务器（同步给所有玩家）。
        /// 【固定尺寸】不再随机，直接返回 FixedSize；历史实现：UnityEngine.Random.Range(1, 4)
        /// </summary>
        /// <returns>决定的尺寸（固定为 FixedSize）</returns>
        public static int DecideSizeLocally()
        {
            ApplySyncedSize(FixedSize);
            OnUploadSize?.Invoke(FixedSize);
            return FixedSize;
        }

        /// <summary>
        /// 按种子决定尺寸。【固定尺寸】忽略种子，直接返回 FixedSize；
        /// 历史实现：new System.Random(roundSeed).Next(1, 4)
        /// </summary>
        public static int RollSize(int roundSeed)
        {
            return FixedSize;
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
        /// 更新 CurrentSize，并同步场上【尚未确认摆放】的旋转吐司实例（拖拽虚影/待摆放的实例，
        /// 需要按本轮尺寸展示占位框与拖拽预览）。
        /// 已确认摆放（Placed != null）的实例保持原尺寸——本轮尺寸只影响本轮新摆放的道具，
        /// 已放置的旧吐司不应被后续轮次的新种子改变大小/占格；
        /// 之后实例化的实例在 Awake 自动读取 CurrentSize。
        /// 【固定尺寸】忽略入参，始终应用 FixedSize（服务器广播/种子结果一律不生效）
        /// </summary>
        public static void ApplySyncedSize(int size)
        {
            CurrentSize = FixedSize;
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                RotatingToast toast = _instances[i];
                if (toast == null)
                {
                    _instances.RemoveAt(i);
                    continue;
                }
                if (toast.Placed != null)
                {
                    continue;   // 已确认摆放的实例：尺寸锁定，不受后续轮次种子影响
                }
                toast.SetSize(CurrentSize);
            }
        }

        /// <summary>新一轮/退出房间时清空（尺寸保持 FixedSize，不回到未决定状态）</summary>
        public static void ClearAll()
        {
            CurrentSize = FixedSize;
            _instances.Clear();
        }
    }
}
