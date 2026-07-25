using System.Collections.Generic;
using UnityEngine;
using SuperQQ.UI;

namespace SuperQQ.Player
{
    /// <summary>
    /// 玩家状态枚举：存活、幽灵、通关
    /// </summary>
    public enum PlayerStateType
    {
        Alive,
        Ghost,
        Finished
    }

    /// <summary>
    /// 玩家管理器 — 模拟服务端角色，全局记录和管理所有玩家的状态
    /// 挂载到场景中任意持久 GameObject 上（如 GameManager）
    /// </summary>
    public class PlayerManager : MonoBehaviour
    {
        // 单例实例
        private static PlayerManager _instance;

        // 玩家列表：按注册顺序保存所有玩家控制器
        private readonly List<PlayerController> _players = new();

        // 玩家状态记录：每个玩家对应的当前状态
        private readonly Dictionary<PlayerController, PlayerStateType> _playerStates = new();

        // 是否处于最后一人存活状态（用于弹窗提示）
        private bool _bIsLastAlivePlayer;

        // 提前放弃弹窗 Prefab 引用，在 Inspector 中设置
        [Header("弹窗配置")]
        [SerializeField] private GameObject _endEarlyPopupPrefab;

        // 当前显示中的提前放弃弹窗控制器引用，避免重复弹出
        private PopupController _endEarlyPopupController;

        // ==================== 单例访问 ====================

        /// <summary>
        /// 全局唯一实例，供外部访问
        /// </summary>
        public static PlayerManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PlayerManager>();
                }
                return _instance;
            }
        }

        // ==================== 公开查询 ====================

        /// <summary>
        /// 当前注册的玩家数量
        /// </summary>
        public int PlayerCount => _players.Count;

        /// <summary>
        /// 获取所有已注册的玩家列表（只读）
        /// </summary>
        public IReadOnlyList<PlayerController> Players => _players.AsReadOnly();

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ==================== 注册与注销 ====================

        /// <summary>
        /// 注册玩家：将玩家添加到管理列表并初始化状态为存活
        /// 由 PlayerController.Start 自动调用
        /// </summary>
        /// <param name="player">待注册的玩家控制器</param>
        public void RegisterPlayer(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            if (_players.Contains(player))
            {
                return;
            }

            _players.Add(player);
            _playerStates[player] = PlayerStateType.Alive;
        }

        /// <summary>
        /// 注销玩家：从管理列表中移除玩家及其状态记录
        /// 由 PlayerController.OnDestroy 自动调用
        /// </summary>
        /// <param name="player">待注销的玩家控制器</param>
        public void UnregisterPlayer(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            _players.Remove(player);
            _playerStates.Remove(player);
        }

        // ==================== 状态更新 ====================

        /// <summary>
        /// 更新玩家状态：由 PlayerController 在状态切换时主动通知
        /// 同时检测是否只剩一名存活玩家
        /// </summary>
        /// <param name="player">状态发生变化的玩家</param>
        /// <param name="stateType">新的状态类型</param>
        public void UpdatePlayerState(PlayerController player, PlayerStateType stateType)
        {
            if (player == null || !_playerStates.ContainsKey(player))
            {
                return;
            }

            _playerStates[player] = stateType;

            // 检测是否只剩一名存活玩家
            CheckLastAlivePlayer();
        }

        /// <summary>
        /// 检测存活玩家数量，当只剩一人时弹出提前放弃提示弹窗
        /// 需要至少两名玩家注册时才触发提示，避免重复弹出
        /// </summary>
        private void CheckLastAlivePlayer()
        {
            bool bWasLastAlive = _bIsLastAlivePlayer;
            _bIsLastAlivePlayer = _players.Count >= 2 && GetAlivePlayerCount() == 1;

            // 仅在从未触发变为触发时弹出弹窗，避免重复弹出
            if (_bIsLastAlivePlayer && !bWasLastAlive)
            {
                ShowEndEarlyPopup();
            }
        }

        /// <summary>
        /// 弹出提前放弃提示弹窗：最顶层显示，3 秒后自动关闭
        /// </summary>
        private void ShowEndEarlyPopup()
        {
            Debug.Log("ShowEndEarlyPopup1");
            
            if (PopupManager.Instance == null || _endEarlyPopupPrefab == null)
            {
                return;
            }
            
            Debug.Log("ShowEndEarlyPopup2");
            
            _endEarlyPopupController = PopupManager.Instance.ShowPopup(
                _endEarlyPopupPrefab,
                autoCloseDuration: 3f,
                onCloseCallback: null,
                parent: null,
                bSortAsTopMost: true);
        }

        // ==================== 状态查询 ====================

        /// <summary>
        /// 获取指定玩家的当前状态
        /// </summary>
        /// <param name="player">目标玩家</param>
        /// <returns>玩家状态，未注册时返回默认值 Alive</returns>
        public PlayerStateType GetPlayerState(PlayerController player)
        {
            if (player != null && _playerStates.TryGetValue(player, out PlayerStateType state))
            {
                return state;
            }
            return PlayerStateType.Alive;
        }

        /// <summary>
        /// 获取处于指定状态的所有玩家
        /// </summary>
        /// <param name="stateType">目标状态类型</param>
        /// <returns>处于该状态的玩家列表</returns>
        public List<PlayerController> GetPlayersByState(PlayerStateType stateType)
        {
            List<PlayerController> result = new();
            for (int i = 0; i < _players.Count; i++)
            {
                if (_playerStates.TryGetValue(_players[i], out PlayerStateType state) && state == stateType)
                {
                    result.Add(_players[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// 判断指定玩家是否处于存活状态
        /// </summary>
        /// <param name="player">目标玩家</param>
        public bool IsPlayerAlive(PlayerController player)
        {
            return GetPlayerState(player) == PlayerStateType.Alive;
        }

        /// <summary>
        /// 判断指定玩家是否处于幽灵状态
        /// </summary>
        /// <param name="player">目标玩家</param>
        public bool IsPlayerGhost(PlayerController player)
        {
            return GetPlayerState(player) == PlayerStateType.Ghost;
        }

        /// <summary>
        /// 判断指定玩家是否已通关
        /// </summary>
        /// <param name="player">目标玩家</param>
        public bool IsPlayerFinished(PlayerController player)
        {
            return GetPlayerState(player) == PlayerStateType.Finished;
        }

        /// <summary>
        /// 判断是否所有玩家都已通关
        /// </summary>
        public bool BAreAllPlayersFinished()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (GetPlayerState(_players[i]) != PlayerStateType.Finished)
                {
                    return false;
                }
            }
            return _players.Count > 0;
        }

        /// <summary>
        /// 判断是否所有玩家都已出局（幽灵或通关）
        /// </summary>
        public bool BAreAllPlayersOut()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                PlayerStateType state = GetPlayerState(_players[i]);
                if (state == PlayerStateType.Alive)
                {
                    return false;
                }
            }
            return _players.Count > 0;
        }

        /// <summary>
        /// 获取存活的玩家数量
        /// </summary>
        public int GetAlivePlayerCount()
        {
            int count = 0;
            for (int i = 0; i < _players.Count; i++)
            {
                if (GetPlayerState(_players[i]) == PlayerStateType.Alive)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 获取已通关的玩家数量
        /// </summary>
        public int GetFinishedPlayerCount()
        {
            int count = 0;
            for (int i = 0; i < _players.Count; i++)
            {
                if (GetPlayerState(_players[i]) == PlayerStateType.Finished)
                {
                    count++;
                }
            }
            return count;
        }

        // ==================== 调试信息 ====================

        /// <summary>
        /// 屏幕左上角依次显示每个玩家的状态信息
        /// </summary>
        private void OnGUI()
        {
            float offsetY = 10f;
            for (int i = 0; i < _players.Count; i++)
            {
                PlayerController player = _players[i];
                PlayerStateType state = GetPlayerState(player);
                string stateName = state.ToString();
                Vector2 pos = player.Rb.position;
                string info = $"[{player.PlayerName}] State: {stateName}  |  Grounded: {player.BIsGrounded}  |  Jumping: {player.BIsJumping}  |  Pos: ({pos.x:F1}, {pos.y:F1})";
                GUI.Label(new Rect(10f, offsetY, 600f, 20f), info);
                offsetY += 20f;
            }
        }
    }
}
