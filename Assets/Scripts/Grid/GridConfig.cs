using UnityEngine;

namespace SuperQQ.Grid
{
    /// <summary>
    /// 网格全局配置 — ScriptableObject 资产
    /// 定义格子的世界尺寸（米），全项目共用一份
    /// 格子尺寸应取最小可摆放块在世界中的边长（如 0.5m x 0.5m 的地块则填 0.5）
    /// 注意：网格逻辑一律使用世界单位，不随分辨率/相机变化
    /// </summary>
    [CreateAssetMenu(fileName = "GridConfig", menuName = "SuperQQ/GridConfig")]
    public class GridConfig : ScriptableObject
    {
        [Header("网格参数")]
        [Tooltip("格子边长（米/世界单位），取最小摆放块的世界尺寸")]
        [SerializeField] private float cellSize = 0.5f;

        /// <summary>
        /// 格子边长（米/世界单位）
        /// </summary>
        public float CellSize => cellSize;
    }
}
