using UnityEngine;

/// <summary>
/// 冰冻特效控制器：挂载在 FX_ShardIce_Explosion_01 根节点。
/// 特效从生成到结束全程持续播放，不做任何暂停（冻结状态由游戏逻辑层面控制，
/// 各子特效 lifetime 设长即可维持悬浮的冰雾画面）。
/// 所有子系统使用非缩放时间（unscaled time），即使游戏通过 Time.timeScale = 0 冻结，特效依然正常播放。
/// 解冻 / 结束特效时调用 Dissipate()：所有子特效统一在 fadeTime 秒内自然消亡。
/// </summary>
public class FrozenIceEffect : MonoBehaviour
{
    [Header("消亡设置")]
    [Tooltip("调用 Dissipate 后，所有子特效统一在该时长（秒）内提前消亡")]
    [SerializeField] private float fadeTime = 0.5f;

    [Header("渲染排序（2D）")]
    [Tooltip("所有子特效的 Sorting Layer，需位于 Player 所在 Layer 之上（列表中越靠后越靠前渲染）")]
    [SortingLayer]
    [SerializeField] private int sortingLayer;

    [Tooltip("所有子特效的 Sorting Order，同 Layer 内需大于 Player 的 order 才能显示在其前面")]
    [SerializeField] private int sortingOrder = 10;

    private ParticleSystem[] _systems;

    /// <summary>是否已开始消亡</summary>
    public bool IsDissipating { get; private set; }

    private void Awake()
    {
        _systems = GetComponentsInChildren<ParticleSystem>(true);

        // 使用非缩放时间：游戏冻结（Time.timeScale = 0）期间特效仍持续播放，
        // 且 Dissipate 的消亡过程不受时间缩放影响。
        foreach (var ps in _systems)
        {
            var main = ps.main;
            main.useUnscaledTime = true;
        }

        // 统一设置所有子特效的 Sorting Layer 与 Order，保证显示在 Player 前面。
        foreach (var renderer in GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            renderer.sortingLayerID = sortingLayer;
            renderer.sortingOrder = sortingOrder;
        }
    }

    /// <summary>
    /// 让所有子特效统一提前消亡：停止发射新粒子，
    /// 并将所有存活粒子的剩余生命压缩到 fadeTime（Inspector 可配）秒内，
    /// 使其走完收尾曲线（透明度淡出 / 缩小）后自然消散。
    /// </summary>
    /// <param name="overrideFadeTime">可选，传入则忽略 Inspector 配置，使用指定时长</param>
    public void Dissipate(float overrideFadeTime = -1f)
    {
        if (IsDissipating)
            return;
        IsDissipating = true;

        float clampedFade = Mathf.Max(overrideFadeTime >= 0f ? overrideFadeTime : fadeTime, 0.01f);

        foreach (var ps in _systems)
        {
            var emission = ps.emission;
            emission.enabled = false;

            int count = ps.particleCount;
            if (count == 0)
                continue;

            var particles = new ParticleSystem.Particle[count];
            ps.GetParticles(particles);

            for (int i = 0; i < count; i++)
            {
                if (particles[i].remainingLifetime > clampedFade)
                    particles[i].remainingLifetime = clampedFade;
            }
            ps.SetParticles(particles, count);
        }
    }
}
