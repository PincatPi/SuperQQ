using System.Collections.Generic;
using SuperQQ.Audio;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 风力区域 — 持续吹风，推动其中的玩家向一个方向位移
    /// 挂在 HitZones 下的 Trigger 物体上（WindZone），区域即吹风范围
    /// 吹风方向取 transform.right：排气扇旋转时自动联动，无需额外配置
    /// 对地面和空中玩家都生效：可助推跳远，也可以把人吹下悬崖
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class WindZone : MonoBehaviour
    {
        [Header("风力")]
        [Tooltip("对玩家的推动加速度（单位/秒²）")]
        [SerializeField] private float windForce = 20f;

        [Header("音效")]
        [Tooltip("吹风循环音效：首个玩家进入吹风范围时开始循环播放，全部离开后音量渐弱至停止（Clip 在 AudioCatalog 资产中按 Id 拖配）；None 表示静默")]
        [SerializeField] private SfxId windLoopSfx = SfxId.FanWind;

        [Tooltip("玩家离开范围后音效淡出时长（秒）")]
        [SerializeField, Min(0.05f)] private float windSfxFadeOut = 0.5f;

        // 区域内玩家计数（多风区重叠时直接累加风力，离开时按进入次数抵消）
        private readonly Dictionary<PlayerController, int> _players = new Dictionary<PlayerController, int>();

        private bool _bSfxPlaying;      // 循环音效当前播放态（边沿触发起停，与区域内有无玩家同步）

        private void Awake()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (!col.isTrigger)
            {
                Debug.LogWarning("[WindZone] 所在碰撞体应为 Trigger", this);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || !player.BAffectedByItems)
            {
                return;   // 死亡过渡/幽灵不受风力影响
            }
            _players.TryGetValue(player, out int count);
            player.AddWindForce(transform.right * windForce);
            _players[player] = count + 1;

            // 首个玩家进入：开始吹风循环音效
            if (count == 0 && windLoopSfx != SfxId.None)
            {
                AudioManager.StartLoopSfx(windLoopSfx);
                _bSfxPlaying = true;
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null)
            {
                return;
            }
            if (!_players.TryGetValue(player, out int count))
            {
                return;
            }
            player.AddWindForce(-transform.right * windForce);
            if (count <= 1)
            {
                _players.Remove(player);
            }
            else
            {
                _players[player] = count - 1;
            }

            // 区域内已无玩家：淡出停止循环音效
            if (_players.Count == 0)
            {
                StopWindSfx();
            }
        }

        private void OnDisable()
        {
            foreach (KeyValuePair<PlayerController, int> pair in _players)
            {
                if (pair.Key != null)
                {
                    pair.Key.AddWindForce(-transform.right * (windForce * pair.Value));
                }
            }
            _players.Clear();
            StopWindSfx();   // 道具禁用/销毁/场景卸载兜底，防止循环通道残留
        }

        /// <summary>淡出停止吹风循环音效（若正在播放）</summary>
        private void StopWindSfx()
        {
            if (_bSfxPlaying)
            {
                _bSfxPlaying = false;
                AudioManager.StopLoopSfx(windLoopSfx, windSfxFadeOut);
            }
        }
    }
}
