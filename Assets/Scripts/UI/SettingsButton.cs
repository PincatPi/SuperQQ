using SuperQQ.Network;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 设置按钮：挂在 Lobby 场景的 ButtonSettings 上，点击后打开设置面板（SettingsPanel）。
    /// 面板中点击"退出登录"：发登出包、清本地登录态并返回主界面。
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SettingsButton : MonoBehaviour
    {
        [Header("设置面板 prefab（根节点需挂 SettingsPanel 脚本）")]
        [SerializeField] private SettingsPanel panelPrefab;

        [Header("退出登录后返回的主界面场景（拖入场景资源，需已加入 Build Settings）")]
#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset homeSceneAsset;
#endif
        [SerializeField, HideInInspector] private string homeSceneName = "Home";

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (homeSceneAsset != null) homeSceneName = homeSceneAsset.name;
        }
#endif

        private Button _button;
        private SettingsPanel _panel;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(OnClicked);
        }

        private void OnDestroy()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnClicked);
            }
        }

        /// <summary>点击设置按钮：打开设置面板（已打开时不重复实例化）</summary>
        private void OnClicked()
        {
            if (_panel != null) return;

            if (panelPrefab == null)
            {
                Debug.LogWarning("[SettingsButton] 未配置设置面板 prefab（panelPrefab）");
                return;
            }

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;

            _panel = SettingsPanel.Show(panelPrefab, canvas.transform, OnLogoutConfirm, () => _panel = null);
        }

        /// <summary>面板中点击"退出登录"：发登出包、清本地登录态并返回主界面</summary>
        private void OnLogoutConfirm()
        {
            _panel = null;
            NetworkManager.Instance?.Logout();
            SceneManager.LoadScene(homeSceneName);
        }
    }
}
