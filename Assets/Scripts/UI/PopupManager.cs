using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.UI
{
    /// <summary>
    /// 弹窗管理器 — 全局管理弹窗的弹出、关闭和对象池复用
    /// 挂载到场景中 UI Canvas 下的 GameObject 上
    /// 对外暴露 ShowPopup 接口，游戏逻辑随时可快捷弹出弹窗
    /// </summary>
    public class PopupManager : MonoBehaviour
    {
        // 单例实例
        private static PopupManager _instance;

        // 弹窗容器：所有弹窗作为此物体的子级，保持层级整洁
        [Header("弹窗配置")]
        [SerializeField] private Transform _popupContainer;

        // 对象池：按 Prefab 索引，复用已关闭的弹窗实例
        private readonly Dictionary<GameObject, Queue<GameObject>> _popupPool = new();

        // 当前活跃的弹窗列表（按弹出顺序排列，后弹出的在列表末尾）
        private readonly List<PopupController> _activePopups = new();

        // Prefab 注册表：记录 Prefab 引用与其名称的映射，用于对象池回收
        private readonly Dictionary<string, GameObject> _prefabRegistry = new();

        // ==================== 单例访问 ====================

        /// <summary>
        /// 全局唯一实例，供外部访问
        /// </summary>
        public static PopupManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PopupManager>();
                }
                return _instance;
            }
        }

        // ==================== 公开查询 ====================

        /// <summary>
        /// 当前活跃弹窗数量
        /// </summary>
        public int ActivePopupCount => _activePopups.Count;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // 若未指定容器，则使用自身作为容器
            if (_popupContainer == null)
            {
                _popupContainer = transform;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            _popupPool.Clear();
            _prefabRegistry.Clear();
        }

        // ==================== 核心接口：弹出弹窗 ====================

        /// <summary>
        /// 弹出弹窗（简写）：使用默认容器和层级，不指定关闭回调
        /// </summary>
        /// <param name="popupPrefab">弹窗 Prefab</param>
        /// <param name="autoCloseDuration">自动关闭时长（秒），0 表示不自动关闭</param>
        /// <returns>弹窗的 PopupController 引用</returns>
        public PopupController ShowPopup(GameObject popupPrefab, float autoCloseDuration = 0f)
        {
            return ShowPopup(popupPrefab, autoCloseDuration, null, null, false);
        }

        /// <summary>
        /// 弹出弹窗（完整参数）：从对象池取出或实例化新弹窗，立即显示
        /// </summary>
        /// <param name="popupPrefab">弹窗 Prefab</param>
        /// <param name="autoCloseDuration">自动关闭时长（秒），0 表示不自动关闭</param>
        /// <param name="onCloseCallback">弹窗关闭时的回调函数，参数为被关闭的 PopupController</param>
        /// <param name="parent">弹窗的父级 Transform，为 null 时使用默认容器</param>
        /// <param name="bSortAsTopMost">是否置于所有弹窗最上层</param>
        /// <returns>弹窗的 PopupController 引用，可用于手动关闭或查询状态</returns>
        public PopupController ShowPopup(
            GameObject popupPrefab,
            float autoCloseDuration,
            Action<PopupController> onCloseCallback,
            Transform parent,
            bool bSortAsTopMost)
        {
            if (popupPrefab == null)
            {
                Debug.LogWarning("[PopupManager] popupPrefab 为空，无法弹出弹窗。");
                return null;
            }

            // 注册 Prefab 以支持对象池回收
            RegisterPrefab(popupPrefab);

            // 确定父级
            Transform actualParent = parent != null ? parent : _popupContainer;

            // 从对象池获取或实例化
            GameObject popupObject = GetFromPool(popupPrefab);
            popupObject.transform.SetParent(actualParent, false);
            popupObject.SetActive(true);

            // 置于最上层
            if (bSortAsTopMost)
            {
                popupObject.transform.SetAsLastSibling();
            }

            // 确保 PopupController 组件存在
            PopupController controller = popupObject.GetComponent<PopupController>();
            if (controller == null)
            {
                controller = popupObject.AddComponent<PopupController>();
            }

            // 包装关闭回调：先执行外部回调，再执行内部回收逻辑
            Action<PopupController> wrappedCallback = OnPopupClosed;
            if (onCloseCallback != null)
            {
                wrappedCallback = (ctrl) =>
                {
                    onCloseCallback.Invoke(ctrl);
                    OnPopupClosed(ctrl);
                };
            }

            // 重置并初始化
            controller.ResetState();
            controller.Initialize(autoCloseDuration, wrappedCallback);

            // 记录到活跃列表
            _activePopups.Add(controller);

            return controller;
        }

        // ==================== 手动关闭 ====================

        /// <summary>
        /// 手动关闭指定弹窗
        /// </summary>
        /// <param name="controller">待关闭的弹窗控制器</param>
        public void ClosePopup(PopupController controller)
        {
            if (controller == null)
            {
                return;
            }

            controller.Close();
        }

        /// <summary>
        /// 关闭所有活跃弹窗
        /// </summary>
        public void CloseAllPopups()
        {
            // 从后往前关闭，避免遍历时修改列表
            for (int i = _activePopups.Count - 1; i >= 0; i--)
            {
                if (_activePopups[i] != null)
                {
                    _activePopups[i].Close();
                }
            }
        }

        // ==================== 内部回调 ====================

        /// <summary>
        /// 弹窗关闭时的内部回调：从活跃列表移除并回收到对象池
        /// </summary>
        /// <param name="controller">被关闭的弹窗控制器</param>
        private void OnPopupClosed(PopupController controller)
        {
            if (controller == null)
            {
                return;
            }

            // 从活跃列表移除
            _activePopups.Remove(controller);

            // 回收到对象池
            ReturnToPool(controller.gameObject);
        }

        // ==================== 对象池 ====================

        /// <summary>
        /// 从对象池获取弹窗实例，池为空时实例化新对象
        /// </summary>
        /// <param name="prefab">弹窗 Prefab</param>
        /// <returns>可用的弹窗 GameObject</returns>
        private GameObject GetFromPool(GameObject prefab)
        {
            if (_popupPool.TryGetValue(prefab, out Queue<GameObject> pool) && pool.Count > 0)
            {
                return pool.Dequeue();
            }

            // 池中无可用实例，创建新对象
            GameObject newInstance = Instantiate(prefab);
            newInstance.name = prefab.name;
            return newInstance;
        }

        /// <summary>
        /// 将弹窗实例回收到对象池，禁用并缓存以供复用
        /// </summary>
        /// <param name="popupObject">待回收的弹窗 GameObject</param>
        private void ReturnToPool(GameObject popupObject)
        {
            if (popupObject == null)
            {
                return;
            }

            popupObject.SetActive(false);

            // 通过名称查找对应的 Prefab 键
            GameObject prefabKey = FindPrefabKeyByName(popupObject.name);
            if (prefabKey == null)
            {
                // 没有对应的 Prefab 注册，直接销毁
                Destroy(popupObject);
                return;
            }

            if (!_popupPool.ContainsKey(prefabKey))
            {
                _popupPool[prefabKey] = new Queue<GameObject>();
            }
            _popupPool[prefabKey].Enqueue(popupObject);
        }

        // ==================== Prefab 注册 ====================

        /// <summary>
        /// 注册 Prefab 到名称映射表，供对象池回收时查找对应 Prefab 键
        /// </summary>
        /// <param name="prefab">待注册的弹窗 Prefab</param>
        private void RegisterPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            if (!_prefabRegistry.ContainsKey(prefab.name))
            {
                _prefabRegistry[prefab.name] = prefab;
            }
        }

        /// <summary>
        /// 通过实例名称查找对应的 Prefab 引用
        /// </summary>
        /// <param name="objectName">弹窗实例的名称</param>
        /// <returns>对应的 Prefab 引用，未找到时返回 null</returns>
        private GameObject FindPrefabKeyByName(string objectName)
        {
            if (_prefabRegistry.TryGetValue(objectName, out GameObject prefab))
            {
                return prefab;
            }
            return null;
        }
    }
}
