using UnityEngine;

namespace SuperQQ.Grid
{
    /// <summary>
    /// 可摆放道具定义 — ScriptableObject 资产
    /// 每种可在建造阶段摆放到网格上的道具对应一份资产
    /// itemId 用于网络传输与注册表查找，必须全局唯一
    /// </summary>
    [CreateAssetMenu(fileName = "PlacableItemDef", menuName = "SuperQQ/PlacableItemDef")]
    public class PlacableItemDef : ScriptableObject
    {
        [Header("标识")]
        [Tooltip("全局唯一ID，网络消息中传输（如 spike_1x1 / platform_2x1）")]
        [SerializeField] private string itemId;

        [Header("预制体")]
        [Tooltip("正式放置时实例化的预制体")]
        [SerializeField] private GameObject prefab;
        [Tooltip("摆放预览用幽灵体（半透明、无碰撞）；留空则运行时由 prefab 自动生成")]
        [SerializeField] private GameObject ghostPrefab;

        [Header("网格占位")]
        [Tooltip("占据的格子数（宽x高），如平台 2x1、风扇 1x3；锚点为 footprint 左下角格子")]
        [SerializeField] private Vector2Int footprint = Vector2Int.one;
        [Tooltip("是否允许旋转90度（旋转后 footprint 宽高互换）")]
        [SerializeField] private bool rotatable;

        [Header("UI")]
        [Tooltip("道具栏图标")]
        [SerializeField] private Sprite icon;

        /// <summary>全局唯一ID（网络传输用）</summary>
        public string ItemId => itemId;
        /// <summary>正式放置时实例化的预制体</summary>
        public GameObject Prefab => prefab;
        /// <summary>摆放预览用幽灵体；为 null 时由 Prefab 自动生成</summary>
        public GameObject GhostPrefab => ghostPrefab;
        /// <summary>未旋转时占据的格子数（宽x高）</summary>
        public Vector2Int Footprint => footprint;
        /// <summary>是否允许旋转90度</summary>
        public bool Rotatable => rotatable;
        /// <summary>道具栏图标</summary>
        public Sprite Icon => icon;

        /// <summary>
        /// 获取旋转后的实际占位尺寸（旋转90度则宽高互换）
        /// </summary>
        public Vector2Int GetFootprint(bool rotated)
        {
            return rotated ? new Vector2Int(footprint.y, footprint.x) : footprint;
        }
    }
}
