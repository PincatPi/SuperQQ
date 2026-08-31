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
        [SerializeField] private PlayerController _playerPrefab;           // 默认玩家预制体，若为空则创建空 GameObject 挂载 PlayerController

        [Header("角色预制体（下标=角色索引，联机按进房顺序分配，互斥）")]
        [Tooltip("四名角色各配一个预制体；角色索引越界时循环复用，条目为空或未配置时回退默认玩家预制体")]
        [SerializeField] private PlayerController[] _characterPrefabs;     // 角色预制体列表，下标即 PlayerProfile.CharacterIndex

        [Header("出生点")]
        [SerializeField] private Transform[] _spawnPoints;                 // 玩家出生点列表，按索引对应玩家序号

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

            // 联机模式每端只有一个本地玩家：上一局/联机流程已建过本地档案（昵称≠预置名，
            // 按名查重会漏）时不再重复注册，否则空 PlayerId 的重复本地档案会残留进结算列表。
            // 单机模式多名本地玩家共用键盘，档案各自独立，不做此合并
            if (player.BIsLocal && BIsNetworkedInRoom())
            {
                IReadOnlyList<PlayerProfile> existing = PlayerSessionManager.Instance.Profiles;
                for (int i = 0; i < existing.Count; i++)
                {
                    if (existing[i] != null && existing[i].IsLocal)
                    {
                        return;
                    }
                }
            }

            PlayerProfile profile = player.BuildProfile();
            PlayerSessionManager.Instance.RegisterProfile(profile);
        }

        /// <summary>当前是否处于"已连接且在房间中"的联机状态</summary>
        private static bool BIsNetworkedInRoom()
        {
            SuperQQ.Network.NetworkManager net = SuperQQ.Network.NetworkManager.Instance;
            return net != null && net.IsConnected && !string.IsNullOrEmpty(net.RoomId);
        }

        /// <summary>
        /// 联机在房时校验远程档案是否属于当前房间（快照优先，JoinedRoom 兜底）。
        /// 不属于即是旧房间残留档案（清理路径遗漏的兜底防线）：不生成化身，
        /// 否则它会因收不到当前房间快照而成为静止的过期 Player。
        /// 房间玩家列表暂不可得（快照未到/无 JoinedRoom）时不拦截，交由后续流程。
        /// </summary>
        private static bool BRemoteProfileOutOfCurrentRoom(string playerId)
        {
            SuperQQ.Network.NetworkManager net = SuperQQ.Network.NetworkManager.Instance;
            if (net == null || !net.IsConnected || string.IsNullOrEmpty(net.RoomId))
            {
                return false; // 离线/未在房不拦截
            }

            System.Collections.Generic.IList<Minigame.Room.V1.RoomPlayerState> players = null;
            SuperQQ.Network.RoomSnapshotReceiver receiver =
                FindFirstObjectByType<SuperQQ.Network.RoomSnapshotReceiver>();
            if (receiver != null && receiver.LatestSnapshot != null
                && receiver.LatestSnapshot.RoomId == net.RoomId && receiver.LatestSnapshot.Players.Count > 0)
            {
                players = receiver.LatestSnapshot.Players;
            }
            else if (net.JoinedRoom != null && net.JoinedRoom.RoomId == net.RoomId
                && net.JoinedRoom.Players.Count > 0)
            {
                players = net.JoinedRoom.Players;
            }

            if (players == null)
            {
                return false; // 房间列表暂不可得，不拦截
            }

            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Player != null && players[i].Player.PlayerId == playerId)
                {
                    return false;
                }
            }
            return true;
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

                // 联机在房时跳过不属于当前房间的远程残留档案（静止过期化身的来源之一）
                if (!profile.IsLocal && !string.IsNullOrEmpty(profile.PlayerId)
                    && BRemoteProfileOutOfCurrentRoom(profile.PlayerId))
                {
                    continue;
                }

                // 主判据是身份（联机 PlayerId / 单机 PlayerName），不是昵称：
                // 换房/续局后服务器可能重新分配 playerId，旧房间的同名残留化身若按名挡掉
                // 新档案，会导致真实玩家化身永远生不出来（SpawnLateJoiner 按身份查档已存在
                // 也直接返回），只剩收不到新房间快照的静止化身
                if (FindPlayerByIdentity(profile.IdentityKey) != null)
                {
                    continue;
                }

                // 同名但身份不同的已注册化身 = 旧房间残留：销毁后按新档案重新生成
                if (!profile.IsLocal && !string.IsNullOrEmpty(profile.PlayerId))
                {
                    PlayerController nameClash = FindPlayerByName(profile.PlayerName);
                    if (nameClash != null && !nameClash.BIsLocal && nameClash.IdentityKey != profile.IdentityKey)
                    {
                        Debug.LogWarning($"[LevelPlayerRegistry] 玩家 {profile.PlayerName} 存在旧身份({nameClash.IdentityKey})的残留化身，销毁后按新身份({profile.IdentityKey})重新生成。", nameClash);
                        Destroy(nameClash.gameObject);
                    }
                }
                // 单机/无 PlayerId 档案保持原按名去重行为（场景预置玩家）
                else if (FindPlayerByName(profile.PlayerName) != null)
                {
                    continue;
                }

                // 本地档案且场景中已有本地玩家对象（预置玩家）：跳过生成。
                // 联机下档案注册可能先于场景加载（昵称大小写/命名不一致时按名去重会失效），
                // 不拦这道会在预置玩家之外多生成一个克隆体（双本地玩家、缩放不一致）
                if (profile.IsLocal)
                {
                    PlayerController existingLocal = FindLocalPlayerObject();
                    if (existingLocal != null && existingLocal.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    if (existingLocal != null)
                    {
                        // 命中的是未激活的预置残留：注册表只收激活对象，它永远不会注册、
                        // 也没有流程会激活它，留着只会把化身生成永远挡掉
                        // （FindLocalPlayerObject 的兜底扫描含未激活对象）——清掉后按档案正常生成
                        foreach (PlayerController stale in FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        {
                            if (stale != null && stale.BIsLocal && !stale.gameObject.activeInHierarchy)
                            {
                                Debug.LogWarning($"[LevelPlayerRegistry] 场景中未激活的本地玩家残留 {stale.name} 会阻挡化身生成，已销毁并改按档案生成。", stale);
                                Destroy(stale.gameObject);
                            }
                        }
                    }
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
            PlayerController prefab = GetCharacterPrefab(profile.CharacterIndex);
            if (prefab != null)
            {
                player = Instantiate(prefab, spawnPosition, spawnRotation);
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
        /// 按角色索引取角色预制体：索引有效且列表已配置时循环取模返回对应条目，
        /// 未指定（-1）/未配置/条目为空时回退默认玩家预制体
        /// </summary>
        public PlayerController GetCharacterPrefab(int characterIndex)
        {
            if (_characterPrefabs != null && _characterPrefabs.Length > 0 && characterIndex >= 0)
            {
                PlayerController prefab = _characterPrefabs[characterIndex % _characterPrefabs.Length];
                if (prefab != null)
                {
                    return prefab;
                }
            }
            return _playerPrefab;
        }

        /// <summary>
        /// 用角色预制体整体替换已有玩家化身（场景预置的本地玩家无法在生成时选预制体，
        /// 联机分配角色索引后走此路径：新实例继承位置/初始出生点/档案，旧实例销毁）。
        /// 已是目标角色（索引一致）或预制体缺失时返回 null，调用方应继续沿用旧化身
        /// </summary>
        /// <param name="oldPlayer">待替换的现有化身</param>
        /// <param name="profile">含目标角色索引的完整档案（键位/身份等会被应用到新化身）</param>
        /// <returns>替换后的新化身；未发生替换返回 null</returns>
        public PlayerController ReplacePlayerAvatar(PlayerController oldPlayer, PlayerProfile profile)
        {
            if (oldPlayer == null || profile == null)
            {
                return null;
            }

            // 幂等：已是目标角色则不重复替换（EnsureRemotePlayersReady 每轮都会调用）
            if (oldPlayer.CharacterIndex == profile.CharacterIndex)
            {
                return null;
            }

            // 仅在角色预制体列表确实配置了该索引的条目时才替换；
            // 未配置列表时回退默认预制体的替换没有意义（与旧化身同款），应沿用旧化身仅着色
            if (_characterPrefabs == null || _characterPrefabs.Length == 0 || profile.CharacterIndex < 0)
            {
                return null;
            }
            PlayerController prefab = _characterPrefabs[profile.CharacterIndex % _characterPrefabs.Length];
            if (prefab == null)
            {
                return null;
            }

            Vector3 position = oldPlayer.transform.position;
            // 继承旧化身的初始出生点：替换后跨轮复活仍回第一轮的实际出生位置
            bool bHadSpawn = _initialSpawnPositions.TryGetValue(oldPlayer, out Vector3 initialSpawn);

            PlayerController player = Instantiate(prefab, position, Quaternion.identity);
            player.ApplyProfile(profile);
            RegisterPlayer(player);
            if (bHadSpawn)
            {
                _initialSpawnPositions[player] = initialSpawn;
            }

            // 旧化身销毁（其 OnDestroy 自动从本注册表/名字标签管理器注销）
            Destroy(oldPlayer.gameObject);
            return player;
        }

        /// <summary>
        /// 获取指定索引的出生点位置
        /// 索引超出范围时返回本对象位置作为默认值
        /// </summary>
        /// <param name="spawnIndex">出生点索引</param>
        private Vector3 GetSpawnPosition(int spawnIndex)
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                return transform.position;
            }
            // 精确匹配
            if (spawnIndex >= 0 && spawnIndex < _spawnPoints.Length && _spawnPoints[spawnIndex] != null)
            {
                return _spawnPoints[spawnIndex].position;
            }
            // 兜底：索引越界（出生点数量少于玩家数）时用最后一个有效出生点，
            // 而非回退到注册表自身位置（注册表可能在场景任意位置，会把玩家甩飞）
            for (int i = _spawnPoints.Length - 1; i >= 0; i--)
            {
                if (_spawnPoints[i] != null)
                {
                    return _spawnPoints[i].position;
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

            // 缓存玩家图标到跨场景档案：图标来自化身 prefab，档案持久化后
            // 最终结算等无化身场景也能展示 PlayerIcon（覆盖预置玩家/生成化身/角色替换全部路径）
            if (PlayerSessionManager.Instance != null)
            {
                PlayerProfile profile = PlayerSessionManager.Instance.GetProfile(player.PlayerName);
                if (profile != null)
                {
                    profile.SelectionIcon = player.SelectionIconSprite;
                }
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
        /// 按身份主键（联机 PlayerId / 单机 PlayerName）查找已注册玩家
        /// 网络同步以 PlayerId 匹配化身，去重必须与此同口径，不能按昵称
        /// </summary>
        public PlayerController FindPlayerByIdentity(string identityKey)
        {
            if (string.IsNullOrEmpty(identityKey))
            {
                return null;
            }

            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i] != null && _players[i].IdentityKey == identityKey)
                {
                    return _players[i];
                }
            }
            return null;
        }

        /// <summary>是否已有本地玩家对象（含场景预置的未激活玩家——注册表只收激活对象，需全场景兜底扫描）</summary>
        public PlayerController FindLocalPlayerObject()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i] != null && _players[i].BIsLocal)
                {
                    return _players[i];
                }
            }
            // 兜底：场景预置玩家可能处于未激活状态（未进注册表），直接扫场景（BIsLocal 读序列化字段，无需 Awake）
            foreach (PlayerController pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (pc.BIsLocal)
                {
                    return pc;
                }
            }
            return null;
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

    }
}
