using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 地图边界组件
    /// 定义可活动区域的矩形范围，提供坐标约束方法
    /// 挂载到场景中的任意 GameObject 上，由 PlayerController 引用
    /// </summary>
    public class MapBoundary : MonoBehaviour
    {
        [Header("地图边界")]
        [SerializeField] private float mapMinX = -10f;                  // 地图左边界X
        [SerializeField] private float mapMaxX = 10f;                   // 地图右边界X
        [SerializeField] private float mapMinY = -10f;                  // 地图下边界Y（掉落死亡线）
        [SerializeField] private float mapMaxY = 10f;                   // 地图上边界Y

        // ==================== 公开访问器 ====================

        public float MinX => mapMinX;
        public float MaxX => mapMaxX;
        public float MinY => mapMinY;
        public float MaxY => mapMaxY;

        // ==================== 约束方法 ====================

        /// <summary>
        /// 将坐标夹紧到地图左右边界内
        /// </summary>
        public Vector2 ClampHorizontal(Vector2 position)
        {
            position.x = Mathf.Clamp(position.x, mapMinX, mapMaxX);
            return position;
        }

        /// <summary>
        /// 将坐标夹紧到地图四周边界内
        /// </summary>
        public Vector2 ClampAll(Vector2 position)
        {
            position.x = Mathf.Clamp(position.x, mapMinX, mapMaxX);
            position.y = Mathf.Clamp(position.y, mapMinY, mapMaxY);
            return position;
        }

        /// <summary>
        /// 判断是否超出地图下边界
        /// </summary>
        public bool IsBelowBoundary(float y)
        {
            return y < mapMinY;
        }

        // ==================== 场景可视化 ====================

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 bl = new Vector3(mapMinX, mapMinY, 0f);
            Vector3 br = new Vector3(mapMaxX, mapMinY, 0f);
            Vector3 tl = new Vector3(mapMinX, mapMaxY, 0f);
            Vector3 tr = new Vector3(mapMaxX, mapMaxY, 0f);
            Gizmos.DrawLine(bl, br);
            Gizmos.DrawLine(br, tr);
            Gizmos.DrawLine(tr, tl);
            Gizmos.DrawLine(tl, bl);
        }
    }
}
