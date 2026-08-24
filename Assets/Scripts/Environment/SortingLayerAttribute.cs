using UnityEngine;

/// <summary>
/// 标记 int 字段为 Sorting Layer 选择器，Inspector 中将以下拉框展示项目已定义的 Sorting Layer。
/// 字段存储的是 SortingLayer.id，运行时通过 renderer.sortingLayerID 应用。
/// </summary>
public class SortingLayerAttribute : PropertyAttribute
{
}
