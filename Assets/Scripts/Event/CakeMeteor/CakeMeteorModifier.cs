using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RandomEventParams = Minigame.Room.V1.RandomEventParams;

namespace SuperQQ.Event
{
    /// <summary>
    /// 小蛋糕陨石事件修饰符 — ScriptableObject 资产
    /// 单机模式：等待首次延迟，随后按随机间隔从陨石生成源落下陨石
    /// （生成源由可配置的中心位置与左右偏移距离构成，方向为竖直向下 ± 随机角度偏移）；
    /// 联机模式（服务端驱动）：生成与运动完全由服务端 RoomSnapshot.event_params1 驱动——
    /// 首个参数包按服务端下发的数量/初始位置/角度/速度实例化本波陨石（实例化点取当前位置），
    /// 后续参数包逐颗做位置校验纠偏，本地只做匀速直线运动的确定性模拟。
    /// 在 Project 窗口选中本资产时，Scene 视图会可视化生成源位置与随机范围（可拖拽调节）
    /// 所有策划参数均在本资产上配置；运行时状态（随机源、协程、生成根节点）不序列化
    /// </summary>
    [CreateAssetMenu(fileName = "CakeMeteorModifier", menuName = "SuperQQ/Event/Cake Meteor Modifier")]
    public class CakeMeteorModifier : LevelEventModifier, IServerDrivenRandomEvent
    {
        [Header("陨石 Prefab")]
        [Tooltip("陨石预制体，需挂载 CakeMeteor 脚本、Trigger 碰撞体与 Kinematic Rigidbody2D")]
        [SerializeField] private CakeMeteor _meteorPrefab;

        [Header("生成节奏")]
        [Tooltip("关卡开始后首次落石的延迟（秒）")]
        [SerializeField] private float _firstDelay = 10f;

        [Tooltip("相邻两颗陨石的最小间隔（秒）")]
        [Min(0f)]
        [SerializeField] private float _minInterval = 5f;

        [Tooltip("相邻两颗陨石的最大间隔（秒）")]
        [Min(0f)]
        [SerializeField] private float _maxInterval = 10f;

        [Tooltip("单次生成时最多落下的陨石数量，每次实际数量在 1~该值之间随机")]
        [Min(1)]
        [SerializeField] private int _maxMeteorsPerSpawn = 3;

        [Header("飞行参数")]
        [Tooltip("陨石下落速度（单位/秒）")]
        [Min(0f)]
        [SerializeField] private float _speed = 8f;

        [Tooltip("下落方向相对竖直向下的最小随机偏移角度（度），实际偏移幅度在 最小~最大 之间随机，方向固定偏左下")]
        [Range(0f, 90f)]
        [SerializeField] private float _minAngleDeviation = 0f;

        [Tooltip("下落方向相对竖直向下的最大随机偏移角度（度），实际偏移幅度在 最小~最大 之间随机，方向固定偏左下")]
        [Range(0f, 90f)]
        [SerializeField] private float _maxAngleDeviation = 20f;

        [Header("生成位置")]
        [Tooltip("陨石生成源的中心位置（世界坐标），陨石从该高度落下")]
        [SerializeField] private Vector2 _spawnCenter = new Vector2(0f, 10f);

        [Tooltip("生成源中心向左的可随机偏移距离，实际生成 X 最小为 中心X-左偏移")]
        [Min(0f)]
        [SerializeField] private float _leftOffset = 3f;

        [Tooltip("生成源中心向右的可随机偏移距离，实际生成 X 最大为 中心X+右偏移")]
        [Min(0f)]
        [SerializeField] private float _rightOffset = 3f;

        [Header("命中效果")]
        [Tooltip("命中玩家时的击飞速度（死亡表现）")]
        [Min(0f)]
        [SerializeField] private float _knockbackSpeed = 12f;

        [Header("消亡兜底")]
        [Tooltip("陨石最大存活时间（秒），超时强制销毁，防止异常情况下物体累积")]
        [Min(0f)]
        [SerializeField] private float _maxLifetime = 15f;

        [Header("随机源（单机模式）")]
        [Tooltip("固定随机种子；为 0 时使用时间种子。联机模式下随机值全部由服务端 event_params1 下发，不消耗本地随机源")]
        [SerializeField] private int _fixedSeed = 0;

        [Header("联机位置校验")]
        [Tooltip("与服务端当前位置的偏差超过该值时直接吸附对齐（防累计漂移/迟到恢复）")]
        [Min(0f)]
        [SerializeField] private float _serverSnapThreshold = 1.5f;

        [Tooltip("偏差在吸附阈值以内时，每包按该系数向服务端位置收敛（0~1，避免快照频率导致的抖动）")]
        [Range(0f, 1f)]
        [SerializeField] private float _serverLerpFactor = 0.3f;

        // ==================== 运行时状态（非序列化，Activate 初始化 / Deactivate 清空） ====================

        // 事件内统一的随机源：生成间隔、生成 X、偏移角度全部走它，不用 UnityEngine.Random
        private System.Random _random;

        // 生成协程引用，Deactivate 时停止
        private Coroutine _spawnCoroutine;

        // 生成的陨石统一挂载的根节点，Deactivate 时整体销毁连带所有存活陨石
        private Transform _spawnRoot;

        // 联机服务端驱动：当前跟踪波次的陨石实例（下标与 event_params1 数组对位）
        private readonly List<CakeMeteor> _serverWave = new List<CakeMeteor>();

        // 联机服务端驱动：当前跟踪波次的初始位置签名（波次身份）——
        // 服务端对同一波次持续下发校验包（初始位置不变），新波次的初始位置不同；
        // 协议暂无 wave_seq 字段，以此区分"同波校验包"与"新波次参数包"
        private readonly List<Vector2> _serverWaveInitials = new List<Vector2>();

        // 联机服务端驱动：是否已有波次在跟踪（同波包校验位置，新波包重新生成）
        private bool _bServerWaveSpawned;

        // ==================== LevelEventModifier 实现 ====================

        /// <summary>
        /// 激活事件：创建随机源与生成根节点，启动陨石生成协程
        /// </summary>
        public override void Activate(LevelEventContext context)
        {
            if (_meteorPrefab == null)
            {
                Debug.LogWarning("[CakeMeteorModifier] 陨石 Prefab 未配置，事件不生效。");
                return;
            }

            if (context == null || context.CoroutineRunner == null)
            {
                Debug.LogWarning("[CakeMeteorModifier] 上下文或协程宿主为空，事件不生效。");
                return;
            }

            // 随机源优先级：上下文下发的服务器种子（联机各端一致）> 资产固定种子 > 时间种子
            int seed = context.RandomSeed != 0 ? context.RandomSeed : _fixedSeed;
            _random = seed != 0 ? new System.Random(seed) : new System.Random();

            // 生成的陨石统一挂到 SceneRoot 下的专用子节点，便于 Deactivate 时统一清理
            GameObject spawnRootObj = new GameObject("CakeMeteorSpawnRoot");
            if (context.SceneRoot != null)
            {
                spawnRootObj.transform.SetParent(context.SceneRoot, false);
            }
            _spawnRoot = spawnRootObj.transform;

            // 联机模式（WaitForTrigger）：只做准备，等服务器触发信号（OnServerTrigger）再开始落石；
            // 单机/自治模式：立即启动生成协程
            if (!context.WaitForTrigger)
            {
                StartSpawnRoutine(context, skipFirstDelay: false);
            }
        }

        /// <summary>
        /// 服务器触发回调：联机模式下服务器掷签的事件触发时刻到达后由 LevelEventAnnouncer 调用。
        /// 服务端驱动模式下本地不再随机生成：波次的数量/位置/角度/速度全部以
        /// RoomSnapshot.event_params1 为准（首个参数包驱动生成），此处仅记录日志。
        /// </summary>
        public override void OnServerTrigger(LevelEventContext context)
        {
            Debug.Log("[CakeMeteorModifier] 服务器触发时刻到达，等待事件参数包（event_params1）驱动生成。");
        }

        // ==================== IServerDrivenRandomEvent 实现（联机服务端驱动） ====================

        /// <summary>
        /// 应用服务端下发的事件参数（RoomSnapshot.event_params1，每次快照到达都可能调用）。
        /// 一个游玩阶段内服务端会连续下发多个波次：以初始位置数组作为波次身份签名——
        /// 与当前跟踪波次签名一致的包为同波校验包，逐颗以 current_positions 做位置纠偏；
        /// 签名不同的包为新波次，重新生成陨石（旧波次陨石保留本地弹道自然落地，不再跟踪）。
        /// 实例化点取 current_positions（触发时刻起服务端已在推进运动），缺失回退 initial_positions。
        /// </summary>
        public void ApplyServerEventParams(RandomEventParams eventParams)
        {
            if (eventParams == null)
            {
                return;
            }

            // 生成前置条件不满足时一次性告警（快照高频重发，不刷屏），供联调定位
            if (_meteorPrefab == null)
            {
                LogSpawnBlockedOnce("陨石预制体未配置");
                return;
            }
            if (_spawnRoot == null)
            {
                LogSpawnBlockedOnce("事件未激活或已停用（生成根节点不存在）");
                return;
            }

            // 有效数量取 count 与两个位置数组的最大值：服务端可能只填 current_positions
            // （initial_positions 为空时不能按 0 丢弃，否则整波静默消失）
            int count = Mathf.Max(eventParams.Count,
                Mathf.Max(eventParams.InitialPositions.Count, eventParams.CurrentPositions.Count));
            if (count <= 0)
            {
                LogSpawnBlockedOnce(
                    $"参数包无有效数据: count={eventParams.Count} initial={eventParams.InitialPositions.Count} current={eventParams.CurrentPositions.Count}");
                return;
            }

            // 首包，或初始位置签名与当前跟踪波次不同（新波次）→ 重新生成
            if (!_bServerWaveSpawned || !IsSameWave(eventParams))
            {
                SpawnServerWave(eventParams, count);
                return;
            }

            // 同波校验包：逐颗以 current_positions 做位置纠偏（已销毁的跳过）
            for (int i = 0; i < _serverWave.Count && i < eventParams.CurrentPositions.Count; i++)
            {
                CakeMeteor meteor = _serverWave[i];
                if (meteor == null)
                {
                    continue;
                }

                Vector2 serverPos = ToWorldPosition(eventParams.CurrentPositions[i]);
                if (!IsPlausibleServerPosition(eventParams, i, serverPos))
                {
                    LogImplausiblePositionOnce(i, serverPos);
                    continue;
                }
                meteor.ApplyServerPosition(serverPos, _serverSnapThreshold, _serverLerpFactor);
            }
        }

        /// <summary>
        /// 判断参数包是否属于当前跟踪的波次：以初始位置数组作为波次身份签名。
        /// 初始位置数组为空时无法判定身份，按同波处理（仅校验，不触发重复生成）；
        /// 服务端对同波持续下发时初始位置逐位一致（同一随机源产出的同一组值）。
        /// </summary>
        private bool IsSameWave(RandomEventParams eventParams)
        {
            if (eventParams.InitialPositions.Count == 0)
            {
                return true;
            }
            if (_serverWaveInitials.Count != eventParams.InitialPositions.Count)
            {
                return false;
            }
            for (int i = 0; i < _serverWaveInitials.Count; i++)
            {
                if (Vector2.Distance(_serverWaveInitials[i], ToWorldPosition(eventParams.InitialPositions[i])) > 0.01f)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>按服务端参数生成新波次（旧波次陨石保留本地弹道继续飞行，仅解除跟踪）</summary>
        private void SpawnServerWave(RandomEventParams eventParams, int count)
        {
            _serverWave.Clear();
            _serverWaveInitials.Clear();

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (i < eventParams.InitialPositions.Count)
                {
                    _serverWaveInitials.Add(ToWorldPosition(eventParams.InitialPositions[i]));
                }

                // 实例化点优先取当前位置（触发时刻起服务端已在推进运动），缺失回退初始位置；
                // 当前位置不可信（偏离初始位置超过最大可飞行距离，如服务端积分时间基错误）时同样回退
                if (!TryGetServerPosition(eventParams, i, out Vector2 spawnPos))
                {
                    Debug.LogWarning($"[CakeMeteorModifier] 第 {i} 颗陨石无位置数据，跳过生成");
                    _serverWave.Add(null);
                    continue;
                }

                CakeMeteor meteor = Instantiate(_meteorPrefab, spawnPos, Quaternion.identity, _spawnRoot);
                meteor.SetWaveIndex(i);
                meteor.Launch(GetServerVelocity(eventParams, i), _knockbackSpeed, _maxLifetime);
                _serverWave.Add(meteor);
                spawned++;
                Debug.Log($"[CakeMeteorModifier] 陨石#{i} 生成位置=({spawnPos.x:F1},{spawnPos.y:F1}) 速度向量={GetServerVelocity(eventParams, i)}");
            }

            _bServerWaveSpawned = true;
            Debug.Log($"[CakeMeteorModifier] 服务端波次已生成: 数量={spawned}/{count} 角度=[{string.Join(", ", eventParams.Angles)}]° 速度={eventParams.Speed:F1} 最大存活={_maxLifetime:F1}s");
        }

        /// <summary>取第 index 颗陨石的位置：当前位置优先，初始位置兜底；两者皆缺返回 false</summary>
        private bool TryGetServerPosition(RandomEventParams eventParams, int index, out Vector2 position)
        {
            if (index < eventParams.CurrentPositions.Count)
            {
                position = ToWorldPosition(eventParams.CurrentPositions[index]);
                if (IsPlausibleServerPosition(eventParams, index, position))
                {
                    return true;
                }
                LogImplausiblePositionOnce(index, position);
            }
            if (index < eventParams.InitialPositions.Count)
            {
                position = ToWorldPosition(eventParams.InitialPositions[index]);
                return true;
            }
            position = default;
            return false;
        }

        /// <summary>
        /// 服务端位置合理性校验：当前位置偏离初始位置的距离不应超过最大可飞行距离
        /// （速度 × 存活时间 × 2 富余）。服务端积分时间基错误（如用纪元秒当运动时长）
        /// 会产生天文数字坐标，此类位置直接丢弃并告警，防止陨石被甩出地图。
        /// 无初始位置作参照时跳过校验（信任当前位置）。
        /// </summary>
        private bool IsPlausibleServerPosition(RandomEventParams eventParams, int index, Vector2 serverPos)
        {
            if (index >= eventParams.InitialPositions.Count)
            {
                return true;
            }
            float speed = eventParams.Speed > 0f ? eventParams.Speed : _speed;
            float maxDistance = speed * (_maxLifetime + 10f) * 2f;
            Vector2 initialPos = ToWorldPosition(eventParams.InitialPositions[index]);
            return Vector2.Distance(serverPos, initialPos) <= maxDistance;
        }

        // 不可信服务端位置已告警过（Deactivate 重置），快照高频重发不重复刷日志
        private bool _bLoggedImplausiblePosition;

        private void LogImplausiblePositionOnce(int index, Vector2 serverPos)
        {
            if (_bLoggedImplausiblePosition)
            {
                return;
            }
            _bLoggedImplausiblePosition = true;
            Debug.LogWarning($"[CakeMeteorModifier] 服务端当前位置不可信已丢弃: 陨石#{index} 位置=({serverPos.x:F1},{serverPos.y:F1}) 偏离初始位置超过最大可飞行距离——请后端检查 current_positions 的运动时长基准（应为 now-triggered_at，疑似误用纪元时间）");
        }

        // 生成受阻原因已告警过（Deactivate 重置），快照高频重发不重复刷日志
        private bool _bLoggedSpawnBlocked;

        private void LogSpawnBlockedOnce(string reason)
        {
            if (_bLoggedSpawnBlocked)
            {
                return;
            }
            _bLoggedSpawnBlocked = true;
            Debug.LogWarning($"[CakeMeteorModifier] 已收到服务端事件参数但无法生成陨石: {reason}");
        }

        /// <summary>
        /// 服务端运动参数换算为指定陨石的飞行速度向量（每颗陨石角度独立，取 angles[index]）。
        /// 角度约定：90°=正下方（75° 偏右下 / 105° 偏左下），与服务端协议注释一致——联调时与后端确认；
        /// 角度数组缺项时回退 90°（正下方）。
        /// </summary>
        private Vector2 GetServerVelocity(RandomEventParams eventParams, int meteorIndex)
        {
            float speed = eventParams.Speed > 0f ? eventParams.Speed : _speed;
            float angleDeg = meteorIndex < eventParams.Angles.Count
                ? eventParams.Angles[meteorIndex]
                : 90f;
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad)) * speed;
        }

        /// <summary>协议 Vector2 → Unity 世界坐标</summary>
        private static Vector2 ToWorldPosition(global::Minigame.Room.V1.Vector2 position)
        {
            return new Vector2(position.X, position.Y);
        }

        /// <summary>启动陨石生成协程（幂等）</summary>
        private void StartSpawnRoutine(LevelEventContext context, bool skipFirstDelay)
        {
            if (_spawnCoroutine != null)
            {
                return;
            }

            _spawnCoroutine = context.CoroutineRunner.StartCoroutine(SpawnMeteorRoutine(skipFirstDelay));
        }

        /// <summary>
        /// 停用事件：停止生成协程，销毁生成根节点（连带所有存活陨石），清空运行时状态
        /// </summary>
        public override void Deactivate(LevelEventContext context)
        {
            if (_spawnCoroutine != null && context != null && context.CoroutineRunner != null)
            {
                context.CoroutineRunner.StopCoroutine(_spawnCoroutine);
                _spawnCoroutine = null;
            }

            if (_spawnRoot != null)
            {
                // 场景正常销毁时根节点可能已随之销毁，此处判空后兜底销毁
                Destroy(_spawnRoot.gameObject);
                _spawnRoot = null;
            }

            // 联机服务端驱动状态随本轮事件一并清空（陨石实体已随根节点销毁）
            _serverWave.Clear();
            _serverWaveInitials.Clear();
            _bServerWaveSpawned = false;
            _bLoggedSpawnBlocked = false;
            _bLoggedImplausiblePosition = false;

            _random = null;
        }

        // ==================== 生成协程 ====================

        /// <summary>
        /// 陨石生成主循环：等待首次延迟后，按随机间隔持续生成陨石
        /// </summary>
        /// <param name="skipFirstDelay">跳过首次延迟（服务器触发模式：触发信号到达即事件开始）</param>
        private IEnumerator SpawnMeteorRoutine(bool skipFirstDelay = false)
        {
            if (!skipFirstDelay)
            {
                yield return new WaitForSeconds(_firstDelay);
            }

            while (true)
            {
                // 单次生成 1~_maxMeteorsPerSpawn 颗陨石（System.Random.Next 上限为开区间）
                int count = _random.Next(1, _maxMeteorsPerSpawn + 1);
                for (int i = 0; i < count; i++)
                {
                    SpawnOneMeteor();
                }

                float interval = Mathf.Lerp(_minInterval, _maxInterval, (float)_random.NextDouble());
                yield return new WaitForSeconds(interval);
            }
        }

        /// <summary>
        /// 生成一颗陨石：从生成源中心左右偏移范围内随机 X 处落下，方向竖直向下 ± 随机角度
        /// </summary>
        private void SpawnOneMeteor()
        {
            float minX = _spawnCenter.x - _leftOffset;
            float maxX = _spawnCenter.x + _rightOffset;
            float spawnX = Mathf.Lerp(minX, maxX, (float)_random.NextDouble());
            Vector2 spawnPos = new Vector2(spawnX, _spawnCenter.y);

            // 偏移幅度在 [最小, 最大] 之间随机，方向固定偏左（负角度，左下方向）
            float angle = -Mathf.Lerp(_minAngleDeviation, _maxAngleDeviation, (float)_random.NextDouble());
            Vector2 direction = Quaternion.Euler(0f, 0f, angle) * Vector2.down;
            Vector2 velocity = direction * _speed;

            CakeMeteor meteor = Instantiate(_meteorPrefab, spawnPos, Quaternion.identity, _spawnRoot);
            meteor.Launch(velocity, _knockbackSpeed, _maxLifetime);
        }
    }
}
