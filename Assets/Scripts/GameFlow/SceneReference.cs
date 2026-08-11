using System;
using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 场景引用。
    /// 在编辑器中拖拽场景资产，运行时使用缓存的场景名称加载场景。
    /// </summary>
    [Serializable]
    public class SceneReference : ISerializationCallbackReceiver
    {
#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset _sceneAsset;
#endif
        [SerializeField, HideInInspector] private string _sceneName = "";

        /// <summary>
        /// 场景名称。
        /// </summary>
        public string SceneName => _sceneName;

        /// <summary>
        /// 是否配置了有效场景。
        /// </summary>
        public bool BIsValid => !string.IsNullOrWhiteSpace(_sceneName);

        public void OnBeforeSerialize()
        {
#if UNITY_EDITOR
            _sceneName = _sceneAsset != null ? _sceneAsset.name : string.Empty;
#endif
        }

        public void OnAfterDeserialize()
        {
        }
    }
}
