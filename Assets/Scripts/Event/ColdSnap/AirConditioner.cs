using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 空调组件 — 挂在场景中的空调 GameObject 上
    /// 负责朝自身本地坐标系的固定方向发射冷气飞行体
    /// 发射方向相对于空调自身的旋转，随空调旋转而变化
    /// 不依赖任何全局管理器，由 ColdSnapModifier 调用 FireColdAir 方法
    /// 每台空调的发射方向在 Inspector 中独立配置
    /// </summary>
    public class AirConditioner : MonoBehaviour
    {
        [Header("发射配置")]
        // 本地坐标系下的发射方向，默认向下
        // 此方向相对于空调自身的旋转，空调旋转时冷气发射方向随之旋转
        [SerializeField] private Vector2 _fireDirection = Vector2.down;

        // 发射点位置，为空时使用自身位置
        [SerializeField] private Transform _firePoint;

        /// <summary>
        /// 发射一发冷气飞行体
        /// 冷气实例化时继承空调的旋转，使冷气的本地坐标系与空调一致
        /// </summary>
        /// <param name="coldAirPrefab">冷气 Prefab</param>
        /// <param name="speed">冷气飞行速度（px/s）</param>
        public void FireColdAir(GameObject coldAirPrefab, float speed)
        {
            if (coldAirPrefab == null)
            {
                Debug.LogWarning("[AirConditioner] 冷气 Prefab 为空，无法发射。");
                return;
            }

            // 确定发射位置：优先使用发射点，否则使用空调自身位置
            Vector3 spawnPosition = _firePoint != null ? _firePoint.position : transform.position;

            // 实例化冷气飞行体，继承空调的旋转
            // 冷气的本地坐标系与空调一致，冷气沿本地方向运动即沿空调本地方向运动
            GameObject coldAirObj = Instantiate(coldAirPrefab, spawnPosition, transform.rotation);

            // 初始化冷气运动参数
            ColdAirProjectile projectile = coldAirObj.GetComponent<ColdAirProjectile>();
            if (projectile == null)
            {
                projectile = coldAirObj.AddComponent<ColdAirProjectile>();
            }

            projectile.Initialize(_fireDirection, speed);
        }
    }
}
