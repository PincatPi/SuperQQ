using System;
using System.Collections.Generic;
using SuperQQ.Score;
using SuperQQ.UI;
using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 关卡玩家注册表 — 场景内单例，管理本关卡中的 PlayerController 实例
    /// 进入关卡场景时根据 PlayerSessionManager 的 Profile 列表实例化玩家
    /// 退出关卡场景时随场景一起销毁，不再持有任何跨场景引用
    /// 解决 PlayerController 跨场景引用失效问题：化身（场景级）与身份（持久级）分离
    /// </summary>
    public class LevelPlayerRegistry : MonoBehaviour
    {
        // 单例实例（场景级，不 DontDestroyOnLoad）
        private static LevelPlayerRegistry _instance;

        // 本关所有 PlayerController 实例，按注册顺序保留
        private readonly List<PlayerController> _players = new();

        // 每名玩家的状态类型，键为 PlayerController 实例
        private readonly Dictionary<PlayerController, PlayerStateType> _playerStates = new();

        // 每名玩家注册时的初始位置（第一轮实际出生点），跨轮复活传送以此为依据，
        // 避免按注册列表下标取出生点与座位下标错位（玩家可能被传送到未配置的点位，例如水里）
        private readonly Dictionary<PlayerController, Vector3> _initialSpawnPositions = new();

        [Header("玩家预制体")]
        [SerializeField] private PlayerController _playerPrefab;           // 玩家预制体，若为空则创建空 GameObject 挂载 PlayerController

        [Header("出生点")]
        [SerializeField] private Transform[] _spawnPoints;                 // 玩家出生点列表，按索引对应玩家序号

        // 仅剩一名存活玩家时触发的提示标记，防止重复弹出
        private bool _bIsLastPlayerStandingTriggered;

        // 提前结束长按时长（秒），对应策划文档：长按蹲/秀键 1.6 秒放弃
        private const float EARLY_QUIT_HOLD_DURATION = 1.6f;

        // ==================== 公开事件 ====================

        /// <summary>
        /// 本关所有玩家出局事件（全员通关或全员死亡/放弃）
        /// 由 SceneManager 订阅以触发结算场景切换
        /// 由 PlayerScoreManager 订阅以触发得分计算
        /// </summary>
        public event Action OnAllPlayersOut;

        /// <summary>
        /// 玩家实例集合变化事件（注册或注销任何 PlayerController 后触发）
        /// 供相机目标组、UI 等表现层模块订阅，与网络/玩法逻辑解耦
        /// </summary>
        public event Action OnPlayersChanged;

        /// <summary>
        /// 玩家状态变更事件（参数：玩家控制器，新状态类型）
        /// 由 UpdatePlayerState 触发，供相机目标组等表现层按状态过滤目标
        /// </summary>
        public event Action<PlayerController, PlayerStateType> OnPlayerStateChanged;

        // ==================== 单例访问 ====================

        /// <summary>
        /// 当前场景中的全局唯一实例
        /// </summary>
        public static LevelPlayerRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<LevelPlayerRegistry>();
                }
                return _instance;
            }
        }

        // ==================== 公开查询 ====================

        /// <summary>
        /// 本关所有玩家（按注册顺序，只读视图）
        /// </summary>
        public IReadOnlyList<PlayerController> Players => _players;

        /// <summary>
        /// 本关玩家数量
        /// </summary>
        public int PlayerCount => _players.Count;

        /// <summary>
        /// 是否只剩一名存活玩家
        /// 满足条件时存活玩家可长按 Down Key 提前放弃
        /// </summary>
        public bool BIsLastPlayerStanding => _bIsLastPlayerStandingTriggered;

        /// <summary>
        /// 提前结束长按时长（秒）
        /// </summary>
        public float EarlyQuitHoldDuration => EARLY_QUIT_HOLD_DURATION;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            // 场景级单例：不 DontDestroyOnLoad，场景卸载时本对象随之销毁
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // 必须在 Awake 中扫描场景中预置的 PlayerController 并同步到 PlayerSessionManager
            // 因为 PlayerScoreManager.HandleSceneLoaded 由 sceneLoaded 事件触发，会在 Start 之前调用
            // InitializeFirstRound 依赖 PlayerSessionManager.Profiles 已填充
            // 若在 Start 中才同步，则首次进入关卡时 PlayerScoreManager 拿到的 Profile 列表为空
            RegisterPresetPlayers();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Start()
        {
            // 关卡场景启动时根据 SessionManager 的档案列表实例化缺失的玩家化身
            // 场景中已预置的玩家在 Awake 中已注册，此处只为缺少化身的档案创建实例
            SpawnMissingPlayerAvatars();
        }

        // ==================== 玩家实例化 ====================

        /// <summary>
        /// 扫描场景中已存在的 PlayerController（手动预置的玩家）
        /// 将其注册到本 Registry，并同步身份到 PlayerSessionManager
        /// 必须在 Awake 中调用，以确保 PlayerScoreManager 在 sceneLoaded 中能拿到完整的 Profile 列表
        /// 按 PlayerName 排序保证注册顺序为 Player1 → Player2 → Player3（结算轨道固定展示顺序）
        /// </summary>
        private void RegisterPresetPlayers()
        {
            PlayerController[] existingPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

            // FindObjectsByType 返回顺序不稳定，按 PlayerName 排序以保证注册顺序与结算展示顺序一致
            System.Array.Sort(existingPlayers, (a, b) =>
                string.Compare(a.PlayerName, b.PlayerName, System.StringComparison.Ordinal));

            for (int i = 0; i < existingPlayers.Length; i++)
            {
                PlayerController player = existingPlayers[i];
                RegisterPlayer(player);
                EnsureProfileForPlayer(player);
            }
        }

        /// <summary>
        /// 为场景中预置的 PlayerController 确保存在对应的 PlayerProfile
        /// 若 PlayerSessionManager 中尚无该名称的档案，则从 PlayerController 构建并注册
        /// 已存在同名档案时跳过，避免重复注册
        /// </summary>
        /// <param name="player">场景中预置的玩家控制器</param>
        private void EnsureProfileForPlayer(PlayerController player)
        {
            if (player == null || string.IsNullOrEmpty(player.PlayerName))
            {
                return;
            }

            if (PlayerSessionManager.Instance == null)
            {
                return;
            }

            if (PlayerSessionManager.Instance.HasPlayer(player.PlayerName))
            {
                return;
            }

            PlayerProfile profile = player.BuildProfile();
            PlayerSessionManager.Instance.RegisterProfile(profile);
        }

        /// <summary>
        /// 根据 PlayerSessionManager 的 Profile 列表在出生点实例化缺失的玩家化身
        /// 已存在的同名玩家（场景中预置的 PlayerController）跳过，避免重复创建
        /// 联机模式下由 NetDebugBootstrap 在进房成功、注册远程玩家档案后再次调用
        /// </summary>
        public void SpawnMissingPlayerAvatars()
        {
            if (PlayerSessionManager.Instance == null)
            {
                Debug.LogWarning("[LevelPlayerRegistry] PlayerSessionManager 不存在，无法实例化玩家。");
                return;
            }

            IReadOnlyList<PlayerProfile> profiles = PlayerSessionManager.Instance.Profiles;
            for (int i = 0; i < profiles.Count; i++)
            {
                PlayerProfile profile = profiles[i];
                if (profile == null || string.IsNullOrEmpty(profile.PlayerName))
                {
                    continue;
                }

                // 跳过已注册的同名玩家（场景中预置的 PlayerController）
                if (FindPlayerByName(profile.PlayerName) != null)
                {
                    continue;
                }

                PlayerController player = CreatePlayerAvatar(profile, i);
                if (player != null)
                {
                    RegisterPlayer(player);
                }
            }
        }

        /// <summary>
        /// 创建一个 PlayerController 化身并应用 Profile 配置
        /// </summary>
        /// <param name="profile">玩家档案</param>
        /// <param name="spawnIndex">出生点索引</param>
        private PlayerController CreatePlayerAvatar(PlayerProfile profile, int spawnIndex)
        {
            Vector3 spawnPosition = GetSpawnPosition(spawnIndex);
            Quaternion spawnRotation = Quaternion.identity;

            PlayerController player;
            if (_playerPrefab != null)
            {
                player = Instantiate(_playerPrefab, spawnPosition, spawnRotation);
            }
            else
            {
                // 无预制体时创建空 GameObject 并挂载 PlayerController
                // 同时补齐 Collider2D 和 SpriteRenderer，避免状态切换时 NullReferenceException
                // 实际项目应通过 Inspector 配置 _playerPrefab 以获得完整视觉效果
                Debug.LogWarning("[LevelPlayerRegistry] 未配置 _playerPrefab，使用空 GameObject 创建玩家。建议配置预制体以获得完整视觉效果。");

                GameObject obj = new GameObject($"Player_{profile.PlayerName}");
                obj.transform.position = spawnPosition;
                obj.tag = "Player";

                // RequireComponent(typeof(Rigidbody2D)) 会自动添加 Rigidbody2D
                // 这里手动补齐 Collider2D 和 SpriteRenderer
                obj.AddComponent<BoxCollider2D>();
                SpriteRenderer renderer = obj.AddComponent<SpriteRenderer>();
                renderer.color = profile.PlayerColor;

                player = obj.AddComponent<PlayerController>();
            }

            // 应用 Profile：设置名称、颜色、键位
            player.ApplyProfile(profile);

            // 联机远程玩家：生成即挂载同步组件（其 Awake 内关闭本地状态机/物理/碰撞/动画驱动）。
            // 否则 RemotePlayerSync 要等 RoomSnapshotReceiver 收到首个快照才挂载，这段窗口内
            // 远程化身会跑本地逻辑：从空中出生点坠落、触发本地淹死/通关判定——
            // 通关态会隐藏渲染器（remote 端又无人恢复），表现为"只有名字没有角色"。
            if (!profile.IsLocal
                && SuperQQ.Network.NetworkManager.Instance != null
                && SuperQQ.Network.NetworkManager.Instance.IsConnected
                && !string.IsNullOrEmpty(SuperQQ.Network.NetworkManager.Instance.RoomId))
            {
                if (player.GetComponent<SuperQQ.Network.RemotePlayerSync>() == null)
                {
                    player.gameObject.AddComponent<SuperQQ.Network.RemotePlayerSync>();
                }
                // PlayerController 已被禁用，其 Start 不再执行（registry 注册由外层
                // SpawnMissingPlayerAvatars 完成），这里补上 Start 中的名字标签注册
                PlayerNameLabelManager.Instance?.RegisterPlayer(player);
            }

            return player;
        }

        /// <summary>
        /// 获取指定索引的出生点位置
        /// 索引超出范围时返回本对象位置作为默认值
        /// </summary>
        /// <param name="spawnIndex">出生点索引</param>
        private Vector3 GetSpawnPosition(int spawnIndex)
        {
            if (_spawnPoints != null && spawnIndex >= 0 && spawnIndex < _spawnPoints.Length)
            {
                if (_spawnPoints[spawnIndex] != null)
                {
                    return _spawnPoints[spawnIndex].position;
                }
            }
            return transform.position;
        }

        /// <summary>
        /// 新一轮开始：复活所有本地玩家并传送回各自出生点。
        /// 联机模式同场景跨轮复用玩家实例，上一轮死亡/通关的玩家仍是幽灵/通关状态，
        /// 必须显式复活回 Alive；远端玩家由各端自己复活后经状态上报同步，本端不处理。
        /// 单机模式每轮换场景生成新实例，Revive 对存活玩家为空操作，可安全调用。
        /// </summary>
        public void ReviveLocalPlayersForNewRound()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                PlayerController player = _players[i];
                if (player == null || !player.BIsLocal)
                {
                    continue;
                }

                player.Revive();

                // 传回第一轮的实际出生点（注册时记录）；缺失时回退到按座位下标取出生点
                Vector3 spawnPosition = _initialSpawnPositions.TryGetValue(player, out Vector3 initialPosition)
                    ? initialPosition
                    : GetSpawnPosition(i);
                player.transform.position = spawnPosition;
                if (player.Rb != null)
                {
                    player.Rb.position = spawnPosition;
                    player.Rb.velocity = Vector2.zero;
                }
            }
        }

        // ==================== 注册与注销 ====================

        /// <summary>
        /// 注册一个 PlayerController 到本关注册表
        /// 由 PlayerController.Start 自动调用，也可由 Spawn 流程主动调用
        /// </summary>
        /// <param name="player">待注册的玩家控制器</param>
        public void RegisterPlayer(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            if (!_players.Contains(player))
            {
                _players.Add(player);
                _playerStates[player] = PlayerStateType.Alive;
                _initialSpawnPositions[player] = player.transform.position;
                OnPlayersChanged?.Invoke();
            }
        }

        /// <summary>
        /// 注销一个 PlayerController
        /// 由 PlayerController.OnDestroy 自动调用
        /// Registry 随场景销毁，不会出现跨场景残留 null 引用问题
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
            OnPlayersChanged?.Invoke();
        }

        // ==================== 状态更新 ====================

        /// <summary>
        /// 更新玩家状态记录
        /// 由 PlayerController 在状态切换时调用
        /// 状态变更后检查是否所有玩家都已出局
        /// </summary>
        /// <param name="player">玩家控制器</param>
        /// <param name="stateType">新状态类型</param>
        public void UpdatePlayerState(PlayerController player, PlayerStateType stateType)
        {
            if (player == null)
            {
                return;
            }

            _playerStates[player] = stateType;
            OnPlayerStateChanged?.Invoke(player, stateType);

            // 检查是否所有玩家都已出局
            CheckAllPlayersOut();
            // 检查是否只剩一名存活玩家，若是则弹出提前结束提示
            CheckLastPlayerStanding();
        }

        // ==================== 查询接口 ====================

        /// <summary>
        /// 按状态类型筛选玩家
        /// 用于结算时获取通关玩家顺序等场景
        /// </summary>
        /// <param name="stateType">目标状态类型</param>
        public List<PlayerController> GetPlayersByState(PlayerStateType stateType)
        {
            List<PlayerController> result = new List<PlayerController>();
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i] == null)
                {
                    continue;
                }

                if (_playerStates.TryGetValue(_players[i], out PlayerStateType state) && state == stateType)
                {
                    result.Add(_players[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// 查询玩家当前状态
        /// 未注册或无记录时按 Alive 返回（与 CheckAllPlayersOut 的"未知按存活"口径一致）
        /// </summary>
        /// <param name="player">玩家控制器</param>
        public PlayerStateType GetPlayerState(PlayerController player)
        {
            if (player != null && _playerStates.TryGetValue(player, out PlayerStateType state))
            {
                return state;
            }
            return PlayerStateType.Alive;
        }

        /// <summary>
        /// 按名称查找玩家
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        public PlayerController FindPlayerByName(string playerName)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                return null;
            }

            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i] != null && _players[i].PlayerName == playerName)
                {
                    return _players[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 获取当前唯一的在场玩家（存活或冻结）
        /// 仅在 BIsLastPlayerStanding 为 true 时有效
        /// 用于提前放弃长按检测时确认当前存活玩家身份
        /// </summary>
        /// <returns>唯一在场玩家的 PlayerController，无在场玩家或多人在场时返回 null</returns>
        public PlayerController GetLastAlivePlayer()
        {
            PlayerController lastAlive = null;
            for (int i = 0; i < _players.Count; i++)
            {
                PlayerController player = _players[i];
                if (player == null)
                {
                    continue;
                }

                if (_playerStates.TryGetValue(player, out PlayerStateType state) && IsInPlay(state))
                {
                    if (lastAlive != null)
                    {
                        // 多于一名在场玩家，返回 null
                        return null;
                    }
                    lastAlive = player;
                }
            }
            return lastAlive;
        }

        /// <summary>
        /// 提前放弃：由最后一名存活玩家长按 Down Key 触发
        /// 该玩家立即死亡（变幽灵），随后由 CheckAllPlayersOut 检测到全员出局并触发结算
        /// </summary>
        /// <param name="player">发起放弃的玩家</param>
        public void TriggerEarlyQuit(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            if (!_bIsLastPlayerStandingTriggered)
            {
                return;
            }

            // 确认该玩家仍是存活状态
            if (!_playerStates.TryGetValue(player, out PlayerStateType state) || state != PlayerStateType.Alive)
            {
                return;
            }

            Debug.Log($"[LevelPlayerRegistry] 玩家 {player.PlayerName} 长按放弃，提前结束本关。");
            player.PlayerDie();
        }

        /// <summary>
        /// 获取玩家颜色
        /// 优先从 PlayerController 读取，失败时回退到 PlayerSessionManager 的 Profile
        /// 用于结算页等需要在玩家化身销毁后仍能取色的场景
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        public Color GetPlayerColor(string playerName)
        {
            PlayerController player = FindPlayerByName(playerName);
            if (player != null)
            {
                return player.PlayerColor;
            }

            if (PlayerSessionManager.Instance != null)
            {
                PlayerProfile profile = PlayerSessionManager.Instance.GetProfile(playerName);
                if (profile != null)
                {
                    return profile.PlayerColor;
                }
            }

            return Color.white;
        }

        // ==================== 出局检测 ====================

        /// <summary>
        /// 判断状态是否视为"在场未出局"：存活或冻结
        /// 冻结玩家仍在场上（解冻后恢复存活），不参与出局/结算判定
        /// </summary>
        private static bool IsInPlay(PlayerStateType state)
        {
            return state == PlayerStateType.Alive || state == PlayerStateType.Frozen;
        }

        /// <summary>
        /// 检查本关是否所有玩家都已出局（通关或死亡）
        /// 至少有一名玩家且无在场玩家时触发 OnAllPlayersOut 事件
        /// </summary>
        private void CheckAllPlayersOut()
        {
            if (_players.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _players.Count; i++)
            {
                PlayerController player = _players[i];
                if (player == null)
                {
                    continue;
                }

                if (_playerStates.TryGetValue(player, out PlayerStateType state))
                {
                    if (IsInPlay(state))
                    {
                        // 仍有在场玩家，未到结算时机
                        return;
                    }
                }
                else
                {
                    // 状态未知，按在场处理
                    return;
                }
            }

            // 所有玩家都已通关或死亡，触发出局事件
            OnAllPlayersOut?.Invoke();
        }

        // ==================== 提前结束检测 ====================

        /// <summary>
        /// 检查本关是否只剩一名存活玩家
        /// 满足条件时通过 PopupManager 弹出 EndEarlyPopup，3 秒后自动关闭
        /// 仅触发一次，避免重复弹出
        /// </summary>
        private void CheckLastPlayerStanding()
        {
            if (_bIsLastPlayerStandingTriggered)
            {
                return;
            }

            if (_players.Count < 2)
            {
                return;
            }

            int aliveCount = 0;
            for (int i = 0; i < _players.Count; i++)
            {
                PlayerController player = _players[i];
                if (player == null)
                {
                    continue;
                }

                if (_playerStates.TryGetValue(player, out PlayerStateType state) && IsInPlay(state))
                {
                    aliveCount++;
                    if (aliveCount > 1)
                    {
                        return;
                    }
                }
            }

            if (aliveCount == 1)
            {
                _bIsLastPlayerStandingTriggered = true;
                ShowEndEarlyPopup();
            }
        }

        /// <summary>
        /// 通过 PopupManager 弹出 EndEarly 弹窗，3 秒后自动关闭并销毁
        /// PopupManager 内部负责实例的创建与销毁
        /// </summary>
        private void ShowEndEarlyPopup()
        {
            if (PopupManager.Instance == null)
            {
                Debug.LogWarning("[LevelPlayerRegistry] PopupManager 不存在，无法弹出提前结束提示。");
                return;
            }

            PopupManager.Instance.ShowPopup(PopupType.EndEarly, PopupArgs.WithDuration(3f));
        }

    }
}
