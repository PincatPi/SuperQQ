using System.Collections;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 小蛋糕陨石事件修饰符 — ScriptableObject 资产
    /// 事件被选中后：等待首次延迟，随后按随机间隔从陨石生成源落下陨石
    /// 生成源由可配置的中心位置与左右偏移距离构成：陨石在 [中心-左偏移, 中心+右偏移] 的
    /// 水平区间内随机 X 坐标生成，方向为竖直向下 ± 随机角度偏移
    /// 在 Project 窗口选中本资产时，Scene 视图会可视化生成源位置与随机范围（可拖拽调节）
    /// 所有策划参数均在本资产上配置；运行时状态（随机源、协程、生成根节点）不序列化
    /// </summary>
    [CreateAssetMenu(fileName = "CakeMeteorModifier", menuName = "SuperQQ/Event/Cake Meteor Modifier")]
    public class CakeMeteorModifier : LevelEventModifier
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

        [Header("随机源")]
        [Tooltip("固定随机种子；为 0 时使用时间种子。联机模式下主机广播该种子即可各端确定性模拟")]
        [SerializeField] private int _fixedSeed = 0;

        // ==================== 运行时状态（非序列化，Activate 初始化 / Deactivate 清空） ====================

        // 事件内统一的随机源：生成间隔、生成 X、偏移角度全部走它，不用 UnityEngine.Random
        private System.Random _random;

        // 生成协程引用，Deactivate 时停止
        private Coroutine _spawnCoroutine;

        // 生成的陨石统一挂载的根节点，Deactivate 时整体销毁连带所有存活陨石
        private Transform _spawnRoot;

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
        /// 触发信号本身即"事件开始"，跳过资产配置的首次延迟，立即开始落石。
        /// </summary>
        public override void OnServerTrigger(LevelEventContext context)
        {
            if (_spawnRoot == null || context == null || context.CoroutineRunner == null)
            {
                return;
            }

            Debug.Log("[CakeMeteorModifier] 服务器触发时刻到达，开始落石。");
            StartSpawnRoutine(context, skipFirstDelay: true);
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
