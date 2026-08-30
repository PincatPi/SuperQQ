using UnityEngine;

namespace SuperQQ
{
    /// <summary>
    /// 应用启动引导：在任何场景加载前执行全局运行参数配置。
    ///
    /// 帧率策略：
    ///   - iOS 上 vSyncCount 无效，帧率由 targetFrameRate 控制，未设置时默认锁 30 帧；
    ///   - Android 上 vSyncCount = 1 时 targetFrameRate 被忽略，帧率跟随屏幕刷新率；
    ///   - 统一设为 60：高刷屏（ProMotion / Android 高刷）想跑 120 改 TargetFrameRate 即可，
    ///     iOS 还需在 Xcode Info.plist 中加 CADisableMinimumFrameDurationOnPhone = YES。
    /// </summary>
    public static class AppBootstrap
    {
        /// <summary>目标帧率</summary>
        private const int TargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            // 关闭垂直同步，让 targetFrameRate 生效（Android）
            QualitySettings.vSyncCount = 0;
            // 解锁 iOS 默认 30 帧限制
            Application.targetFrameRate = TargetFrameRate;

            Debug.Log($"[AppBootstrap] 帧率配置完成：vSyncCount = 0, targetFrameRate = {TargetFrameRate}");
        }
    }
}
