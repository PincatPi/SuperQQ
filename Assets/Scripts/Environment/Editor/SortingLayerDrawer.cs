using UnityEditor;
using UnityEngine;

/// <summary>
/// SortingLayerAttribute 的 Inspector 绘制器：以 Popup 下拉框展示项目已定义的所有 Sorting Layer。
/// </summary>
[CustomPropertyDrawer(typeof(SortingLayerAttribute))]
public class SortingLayerDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.Integer)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        EditorGUI.BeginProperty(position, label, property);

        SortingLayer[] layers = SortingLayer.layers;
        string[] names = new string[layers.Length];
        int currentIndex = 0;

        for (int i = 0; i < layers.Length; i++)
        {
            names[i] = layers[i].name;
            if (layers[i].id == property.intValue)
                currentIndex = i;
        }

        int newIndex = EditorGUI.Popup(position, label.text, currentIndex, names);
        property.intValue = layers[newIndex].id;

        EditorGUI.EndProperty();
    }
}
