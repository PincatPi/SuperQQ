using Minigame.Room.V1;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Network
{
    /// <summary>
    /// 出局上报器（与 InputReporter 一样由 NetDebugBootstrap 挂在本地玩家身上）。
    /// 轮询本地玩家状态机：通关（Finished）或死亡/幽灵（Ghost）首次发生时，
    /// 立即向服务器发送 PlayerOutReport，供服务器裁决名次与判定全员出局。
    /// 每轮（阶段切换后）允许重新上报：新一轮开始时玩家状态回到 Alive。
    /// </summary>
    public class PlayerOutReporter : MonoBehaviour
    {
        private PlayerController _player;
        private bool _reported;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
        }

        private void Update()
        {
            if (_reported || _player == null) return;

            NetworkManager net = NetworkManager.Instance;
            if (net == null || !net.IsConnected || string.IsNullOrEmpty(net.RoomId)) return;

            PlayerOutType outType = PlayerOutType.Unspecified;
            if (_player.BIsFinished)
            {
                outType = PlayerOutType.Finished;
            }
            else if (_player.BIsDead || _player.BIsGhost)
            {
                outType = PlayerOutType.Dead;
            }

            if (outType == PlayerOutType.Unspecified) return;

            _reported = true;
            net.Send(new PlayerOutReport
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                OutType = outType,
                ClientTimeMs = NetworkManager.NowMs()
            });
            Debug.Log($"[NetWork] 已上报出局: type={outType}");
        }

        /// <summary>新一轮开始时重置（玩家状态回到 Alive 后由阶段驱动调用，或自动检测）</summary>
        private void LateUpdate()
        {
            // 自动复位：玩家回到存活状态说明已进入新一轮
            if (_reported && _player != null && !_player.BIsFinished && !_player.BIsDead && !_player.BIsGhost)
            {
                _reported = false;
            }
        }
    }
}
