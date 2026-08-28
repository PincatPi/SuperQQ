using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 退出游戏按钮：点击后退出应用。
    /// 在编辑器中点击会停止播放模式，打包后会调用 Application.Quit()。
    /// </summary>
    public class ExitGameButton : MonoBehaviour
    {
        [SerializeField] private Button exitButton;

        private void Awake()
        {
            // 未在 Inspector 指定时，优先使用挂载在同一物体上的 Button
            if (exitButton == null)
            {
                exitButton = GetComponent<Button>();
            }

            if (exitButton != null)
            {
                exitButton.onClick.AddListener(OnExitClicked);
            }
            else
            {
                Debug.LogWarning("[ExitGameButton] 未找到 Button 组件，请在 Inspector 中指定 exitButton。");
            }
        }

        private void OnDestroy()
        {
            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(OnExitClicked);
            }
        }

        private void OnExitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
