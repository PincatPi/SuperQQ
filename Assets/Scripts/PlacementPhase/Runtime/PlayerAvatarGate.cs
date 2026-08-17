using System.Collections.Generic;
using SuperQQ.Player;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Placement.Runtime
{
    /// <summary>
    /// 角色隐藏与移动输入屏蔽（纯 C# 辅助类，由 PropPlacementDirector 持有）。
    /// 放置阶段角色不出现在场上、也不响应移动操作：
    /// 采用「缓存原值 → 注入空输入 + 关闭表现与物理」的方式，退出阶段后原样还原。
    /// 不销毁、不 SetActive(false)，避免破坏玩家注册表、计分与状态机内部计时。
    /// </summary>
    public class PlayerAvatarGate
    {
        /// <summary>单个角色被屏蔽前的原始状态</summary>
        private struct AvatarState
        {
            public IPlayerInput Input;
            public bool BRendererEnabled;
            public bool BColliderEnabled;
            public bool BRbSimulated;
        }

        private readonly Dictionary<PlayerController, AvatarState> cache =
            new Dictionary<PlayerController, AvatarState>();

        /// <summary>当前是否处于屏蔽状态</summary>
        public bool BIsSuppressed => cache.Count > 0;

        /// <summary>
        /// 屏蔽全部关卡内角色：注入空输入、关闭渲染与碰撞、停止物理模拟并隐藏名称标签。
        /// 对已屏蔽的角色为空操作，可安全重复调用。
        /// </summary>
        public void Suppress()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return;
            }

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player == null || cache.ContainsKey(player))
                {
                    continue;
                }

                cache[player] = new AvatarState
                {
                    Input = player.InputSource,
                    BRendererEnabled = player.Renderer != null && player.Renderer.enabled,
                    BColliderEnabled = player.Collider != null && player.Collider.enabled,
                    BRbSimulated = player.Rb != null && player.Rb.simulated,
                };

                player.SetInputSource(NullPlayerInput.Instance);
                ApplyVisibility(player, bVisible: false);
                PlayerNameLabelManager.Instance?.UnregisterPlayer(player);
            }
        }

        /// <summary>还原全部被屏蔽角色的输入、表现与名称标签</summary>
        public void Restore()
        {
            foreach (KeyValuePair<PlayerController, AvatarState> pair in cache)
            {
                PlayerController player = pair.Key;
                if (player == null)
                {
                    continue;   // 阶段进行中被销毁的角色无需还原
                }

                AvatarState state = pair.Value;
                player.SetInputSource(state.Input);
                if (player.Renderer != null)
                {
                    player.Renderer.enabled = state.BRendererEnabled;
                }
                if (player.Collider != null)
                {
                    player.Collider.enabled = state.BColliderEnabled;
                }
                if (player.Rb != null)
                {
                    player.Rb.simulated = state.BRbSimulated;
                }
                PlayerNameLabelManager.Instance?.RegisterPlayer(player);
            }

            cache.Clear();
        }

        private static void ApplyVisibility(PlayerController player, bool bVisible)
        {
            if (player.Renderer != null)
            {
                player.Renderer.enabled = bVisible;
            }
            if (player.Collider != null)
            {
                player.Collider.enabled = bVisible;
            }
            if (player.Rb != null)
            {
                // 停止物理模拟：角色不受重力影响，位置在整个放置阶段保持不动
                player.Rb.simulated = bVisible;
            }
        }
    }
}
