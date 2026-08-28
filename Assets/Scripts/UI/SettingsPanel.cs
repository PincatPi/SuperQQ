using System;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 设置面板：挂在设置面板 prefab 的根节点上。
    /// 面板内需包含一个"退出登录"按钮（logoutButton）；
    /// 若未在 Inspector 指定，会按名字自动查找子物体中的按钮
    /// （名字包含 "Logout" 或 "退出登录"）。
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        [SerializeField] private Button logoutButton;
        [SerializeField] private Button closeButton;

        private Action _onLogout;
        private Action _onClosed;

        /// <summary>在 parent（一般为 Canvas）下实例化并显示设置面板</summary>
        public static SettingsPanel Show(SettingsPanel prefab, Transform parent, Action onLogout, Action onClosed = null)
        {
            SettingsPanel panel = Instantiate(prefab, parent, false);
            panel._onLogout = onLogout;
            panel._onClosed = onClosed;
            panel.BindButtons();
            return panel;
        }

        private void BindButtons()
        {
            if (logoutButton == null)
            {
                logoutButton = FindButtonByName("Logout", "退出登录");
            }

            if (logoutButton != null)
            {
                logoutButton.onClick.AddListener(OnLogoutClicked);
            }
            else
            {
                Debug.LogWarning("[SettingsPanel] 未找到\"退出登录\"按钮，请在 Inspector 中指定 logoutButton。");
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(OnCloseClicked);
            }
        }

        /// <summary>点击"退出登录"：先关闭面板，再回调给调用方执行登出流程</summary>
        private void OnLogoutClicked()
        {
            Close();
            _onLogout?.Invoke();
        }

        private void OnCloseClicked()
        {
            Close();
        }

        public void Close()
        {
            _onClosed?.Invoke();
            Destroy(gameObject);
        }

        private Button FindButtonByName(params string[] keywords)
        {
            foreach (Button button in GetComponentsInChildren<Button>(true))
            {
                foreach (string keyword in keywords)
                {
                    if (button.name.Contains(keyword))
                    {
                        return button;
                    }
                }
            }
            return null;
        }
    }
}
