using UnityEngine;

namespace SuperQQ.Sensors
{
    /// <summary>
    /// 摇晃检测器 — 场景级独立输入模块，与具体玩法完全解耦
    /// 基于加速度计（Input.acceleration）感知摇晃：低通滤波分离重力分量后，
    /// 取剩余加速度变化量作为摇晃信号，经指数平滑映射为归一化强度 CurrentIntensity（0~1）
    /// 选加速度计而非陀螺仪：无需额外开启传感器，"摇晃"语义上直接对应加速度剧烈变化
    ///
    /// 使用方式：
    ///   - 消费者通过 GetOrCreate() 获取实例（场景中无则自动创建，不依赖场景预置）
    ///   - 仅在需要检测的阶段 enable（如解冻阶段），平时保持关闭避免无谓轮询
    ///   - 每帧轮询 CurrentIntensity 即可，本模块不感知任何游戏逻辑
    ///
    /// Editor 调试：Editor 无加速度计（SystemInfo.supportsAccelerometer 为 false）时，
    /// 输出值由 Debug Intensity Override 滑条模拟，可在 Inspector 中拖动调试
    /// </summary>
    [DisallowMultipleComponent]
    public class ShakeDetector : MonoBehaviour
    {
        [Header("灵敏度")]
        [Tooltip("静止阈值：低于此加速度变化量视为未摇晃（过滤重力估计误差与手部微颤），单位 m/s²")]
        [Min(0f)]
        [SerializeField] private float _idleThreshold = 0.6f;

        [Tooltip("满强度阈值：加速度变化量达到该值时输出满强度 1，单位 m/s²（剧烈摇晃约 3~5）")]
        [Min(0.01f)]
        [SerializeField] private float _fullIntensityThreshold = 4f;

        [Header("滤波")]
        [Tooltip("重力估计的跟随速度（低通滤波）：越大越快跟随持机姿态变化，过小会把缓慢转动机误判为摇晃")]
        [Min(0.1f)]
        [SerializeField] private float _gravityAdaptSpeed = 3f;

        [Tooltip("强度输出的平滑速度（指数平滑）：越大输出越跟手，越小越平稳")]
        [Min(0.1f)]
        [SerializeField] private float _smoothingSpeed = 10f;

        [Header("Editor 调试")]
        [Tooltip("无加速度计的环境（如 Editor）下，用该值模拟摇晃强度输出")]
        [Range(0f, 1f)]
        [SerializeField] private float _debugIntensityOverride = 0f;

        // 重力分量的低通估计值
        private Vector3 _gravityEstimate;

        // 重力估计是否已初始化（首帧直接以采样值初始化，避免从零收敛的启动毛刺）
        private bool _bHasGravityEstimate;

        // ==================== 公开查询 ====================

        /// <summary>
        /// 当前摇晃强度（0~1，指数平滑后的值）：0 = 静止，1 = 剧烈摇晃
        /// 组件被禁用时恒为 0
        /// </summary>
        public float CurrentIntensity { get; private set; }

        /// <summary>
        /// 当前设备是否支持加速度计；不支持时输出走 Debug Intensity Override 模拟
        /// </summary>
        public bool BIsSensorAvailable => SystemInfo.supportsAccelerometer;

        // ==================== 实例获取 ====================

        /// <summary>
        /// 获取场景中的 ShakeDetector，无则自动创建一个（默认禁用，需要时由消费者启用）
        /// </summary>
        public static ShakeDetector GetOrCreate()
        {
            ShakeDetector detector = FindFirstObjectByType<ShakeDetector>();
            if (detector == null)
            {
                detector = new GameObject(nameof(ShakeDetector)).AddComponent<ShakeDetector>();
                detector.enabled = false;
            }
            return detector;
        }

        // ==================== 生命周期 ====================

        private void OnEnable()
        {
            // 启用时重置滤波状态：以首帧采样重建重力估计，强度从 0 开始爬升
            _bHasGravityEstimate = false;
            CurrentIntensity = 0f;
        }

        private void OnDisable()
        {
            // 禁用即无输入语义：输出归零，避免消费者读到残留强度
            CurrentIntensity = 0f;
        }

        private void Update()
        {
            float target = SampleRawIntensity();
            // 帧率无关的指数平滑：强度向本帧采样值收敛
            float lerpFactor = 1f - Mathf.Exp(-_smoothingSpeed * Time.deltaTime);
            CurrentIntensity = Mathf.Lerp(CurrentIntensity, target, lerpFactor);
        }

        // ==================== 采样 ====================

        /// <summary>
        /// 采样本帧的原始摇晃强度（未平滑）：
        /// 加速度计读数中分离重力分量，剩余变化量映射到 0~1
        /// </summary>
        private float SampleRawIntensity()
        {
            if (!BIsSensorAvailable)
            {
                return _debugIntensityOverride;
            }

            Vector3 acceleration = UnityEngine.Input.acceleration;

            // 低通滤波估计重力分量：缓慢跟随持机姿态，隔离出"摇晃"引入的快速变化
            if (!_bHasGravityEstimate)
            {
                _gravityEstimate = acceleration;
                _bHasGravityEstimate = true;
                return 0f;
            }

            float gravityLerpFactor = 1f - Mathf.Exp(-_gravityAdaptSpeed * Time.deltaTime);
            _gravityEstimate = Vector3.Lerp(_gravityEstimate, acceleration, gravityLerpFactor);

            float shakeMagnitude = (acceleration - _gravityEstimate).magnitude;
            return Mathf.InverseLerp(_idleThreshold, _fullIntensityThreshold, shakeMagnitude);
        }
    }
}
