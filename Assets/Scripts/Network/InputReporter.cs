using Minigame.Room.V1;
using SuperQQ.Player;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

namespace SuperQQ.Network
{
    /// <summary>
    /// 本地玩家状态上报器（挂在本地玩家的 PlayerController 同物体上）。
    /// 按固定频率（默认 20Hz）把自身 TransformState 通过 SyncPlayerStateRequest 上报网关，
    /// 服务器覆盖缓存后向全房间广播 RoomSnapshot。
    ///
    /// 注意：
    /// - 只挂在 IsLocal 玩家身上；远程玩家由 RoomSnapshotReceiver 驱动，不上报
    /// - 上报内容是"状态"（位置/朝向/速度），与后端协议 SyncPlayerStateRequest 对齐
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class InputReporter : MonoBehaviour
    {
        [Header("上报频率（次/秒）")]
        [SerializeField] private float reportRate = 20f;

        private PlayerController _player;
        private Rigidbody2D _rb;
        private SpriteRenderer _renderer;
        private float _timer;
        private ulong _stateSeq;
        private bool _firstReportLogged;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _rb = GetComponent<Rigidbody2D>();
            _renderer = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            NetworkManager net = NetworkManager.Instance;
            if (net == null || !net.IsConnected) return;
            if (string.IsNullOrEmpty(net.RoomId) || string.IsNullOrEmpty(net.LocalPlayerId)) return;

            _timer += Time.deltaTime;
            float interval = 1f / reportRate;
            if (_timer < interval) return;
            _timer = 0f;

            if (!_firstReportLogged)
            {
                _firstReportLogged = true;
                Debug.Log($"[NetWork] 开始上报本地玩家状态: playerId={net.LocalPlayerId} room={net.RoomId} 频率={reportRate}Hz");
            }

            Vector2 pos = transform.position;
            Vector2 vel = _rb != null ? _rb.velocity : Vector2.zero;

            // 玩家状态：0=存活 1=幽灵 2=已通关（与 proto player_state 约定一致）
            int playerState = _player.BIsDead ? 1 : _player.BIsFinished ? 2 : 0;

            net.Send(new SyncPlayerStateRequest
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                Transform = new TransformState
                {
                    Position = new Minigame.Room.V1.Vector2 { X = pos.x, Y = pos.y },
                    Velocity = new Minigame.Room.V1.Vector2 { X = vel.x, Y = vel.y },
                    Direction = new Minigame.Room.V1.Vector2
                    {
                        X = _player.HorizontalInput,
                        Y = _player.VerticalInput
                    },
                    StateSeq = ++_stateSeq,
                    ClientTimeMs = NetworkManager.NowMs(),
                    PlayerState = playerState,
                    FacingLeft = _renderer != null && _renderer.flipX
                }
            });
        }
    }
}
