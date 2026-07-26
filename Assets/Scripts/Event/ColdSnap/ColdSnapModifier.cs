using System.Collections;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// ColdSnap（空调变冷）事件修饰符 — ScriptableObject 资产
    /// 对应策划文档 9.2 节：关卡开始后延迟预警，然后四台空调依次发射冷气
    /// 本类负责时间调度和空调发射控制，冷气命中和玩家冻结逻辑为次要部分，预留扩展
    /// 所有时间参数、冷气参数和空调组 Prefab 在 Inspector 中配置，便于策划调优
    /// 空调组在 Activate 时动态实例化，Deactivate 时销毁，不预先放置在场景中
    /// </summary>
    [CreateAssetMenu(fileName = "ColdSnapModifier", menuName = "SuperQQ/ColdSnapModifier")]
    public class ColdSnapModifier : LevelEventModifier
    {
        [Header("时间节奏（秒）")]
        // 关卡开始后多久进入冷气预警
        [SerializeField] private float _delayBeforeWarning = 5.2f;

        // 冷气预警持续时长
        [SerializeField] private float _warningDuration = 1.5f;

        // 冷气发射阶段持续时长
        [SerializeField] private float _activeDuration = 14f;

        // 空调依次发射冷气的间隔
        [SerializeField] private float _fireInterval = 1.45f;

        [Header("冷气参数")]
        // 冷气飞行固定速度（px/s）
        [SerializeField] private float _coldAirSpeed = 135f;

        // 冷气飞行体 Prefab
        [SerializeField] private GameObject _coldAirPrefab;

        [Header("空调资源")]
        // 空调组 Prefab：包含多台空调的父节点，各空调方向在 Prefab 中配置
        // Activate 时动态实例化，Deactivate 时销毁，不预先放置在场景中
        [SerializeField] private GameObject _airConditionerGroupPrefab;

        // 当前运行的协程引用，用于 Deactivate 时停止
        private Coroutine _coldSnapCoroutine;

        // 当前上下文引用，用于 Deactivate 时停止协程
        private LevelEventContext _currentContext;

        // 空调组运行时实例引用，用于 Deactivate 时销毁
        private GameObject _airConditionerGroupInstance;

        // 缓存实例中所有空调组件，避免发射阶段重复查找
        private AirConditioner[] _airConditioners;

        /// <summary>
        /// 激活 ColdSnap 事件：实例化空调组并启动冷气发射流程协程
        /// </summary>
        /// <param name="context">运行时上下文，提供协程宿主</param>
        public override void Activate(LevelEventContext context)
        {
            if (context == null || context.CoroutineRunner == null)
            {
                Debug.LogWarning("[ColdSnapModifier] 上下文或协程宿主为空，无法激活。");
                return;
            }

            if (_coldAirPrefab == null)
            {
                Debug.LogWarning("[ColdSnapModifier] 冷气 Prefab 未配置，无法激活。");
                return;
            }

            if (_airConditionerGroupPrefab == null)
            {
                Debug.LogWarning("[ColdSnapModifier] 空调组 Prefab 未配置，无法激活。");
                return;
            }

            _currentContext = context;

            // 动态实例化空调组到场景根目录
            _airConditionerGroupInstance = Instantiate(_airConditionerGroupPrefab);

            // 缓存实例中所有空调组件，供发射阶段使用
            _airConditioners = _airConditionerGroupInstance.GetComponentsInChildren<AirConditioner>();

            if (_airConditioners.Length == 0)
            {
                Debug.LogWarning("[ColdSnapModifier] 空调组 Prefab 中未找到 AirConditioner 组件，终止激活。");
                Destroy(_airConditionerGroupInstance);
                _airConditionerGroupInstance = null;
                return;
            }

            _coldSnapCoroutine = context.CoroutineRunner.StartCoroutine(RunColdSnap());
        }

        /// <summary>
        /// 停用 ColdSnap 事件：停止冷气发射协程并销毁空调组实例
        /// </summary>
        /// <param name="context">运行时上下文</param>
        public override void Deactivate(LevelEventContext context)
        {
            // 停止协程
            if (_coldSnapCoroutine != null && context != null && context.CoroutineRunner != null)
            {
                context.CoroutineRunner.StopCoroutine(_coldSnapCoroutine);
                _coldSnapCoroutine = null;
            }

            // 销毁空调组实例，释放场景资源
            if (_airConditionerGroupInstance != null)
            {
                Destroy(_airConditionerGroupInstance);
                _airConditionerGroupInstance = null;
            }

            _airConditioners = null;
            _currentContext = null;
        }

        /// <summary>
        /// ColdSnap 事件主流程协程
        /// 阶段1：等待 DelayBeforeWarning 秒
        /// 阶段2：预警阶段（次要部分，目前仅日志）
        /// 阶段3：在 ActiveDuration 秒内，按 FireInterval 间隔依次让空调发射冷气
        /// 阶段4：结束清理
        /// </summary>
        private IEnumerator RunColdSnap()
        {
            // 阶段1：关卡开始后等待
            yield return new WaitForSeconds(_delayBeforeWarning);

            // 阶段2：冷气预警（次要部分，目前仅输出日志）
            Debug.Log("[ColdSnapModifier] 冷气预警开始：空调即将喷出冷气！");
            yield return new WaitForSeconds(_warningDuration);

            // 阶段3：冷气发射阶段
            if (_airConditioners == null || _airConditioners.Length == 0)
            {
                Debug.LogWarning("[ColdSnapModifier] 空调组件缓存为空，终止冷气发射。");
                yield break;
            }

            Debug.Log($"[ColdSnapModifier] 冷气发射阶段开始，共 {_airConditioners.Length} 台空调。");

            float endTime = Time.time + _activeDuration;
            int acIndex = 0;

            while (Time.time < endTime)
            {
                FireFromAirConditioner(_airConditioners[acIndex]);

                // 循环到下一台空调
                acIndex = (acIndex + 1) % _airConditioners.Length;

                yield return new WaitForSeconds(_fireInterval);
            }

            // 阶段4：结束
            Debug.Log("[ColdSnapModifier] 冷气发射阶段结束。");
            _coldSnapCoroutine = null;
        }

        /// <summary>
        /// 让指定空调发射一发冷气，使用配置的固定速度
        /// </summary>
        /// <param name="airConditioner">要发射冷气的空调</param>
        private void FireFromAirConditioner(AirConditioner airConditioner)
        {
            if (airConditioner == null)
            {
                return;
            }

            airConditioner.FireColdAir(_coldAirPrefab, _coldAirSpeed);
        }
    }
}
