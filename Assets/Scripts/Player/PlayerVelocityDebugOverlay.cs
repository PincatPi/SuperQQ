using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 速度调试浮层 — 运行时屏幕左上角实时打印角色刚体速度
    /// 独立调试组件：只读取 Rigidbody2D 速度，不写任何状态，不与其他逻辑耦合
    /// 不需要时直接在物体上禁用/移除本组件即可，对项目零影响
    /// </summary>
    public class PlayerVelocityDebugOverlay : MonoBehaviour
    {
        [Header("目标")]
        [SerializeField] private Rigidbody2D target;                 // 目标刚体（不填则自动从父级/自身查找）

        [Header("显示设置")]
        [SerializeField] private int fontSize = 32;                  // 字体大小
        [SerializeField] private Vector2 screenPosition = new Vector2(10f, 10f); // 屏幕左上角偏移

        private GUIStyle _style;

        private void Awake()
        {
            if (target == null)
            {
                target = GetComponentInParent<Rigidbody2D>();
            }

            if (target == null)
            {
                Debug.LogWarning("[PlayerVelocityDebugOverlay] 未找到目标 Rigidbody2D，请手动指定。", this);
                enabled = false;
            }
        }

        private void OnGUI()
        {
            if (target == null)
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    normal = { textColor = Color.black }
                };
            }

            Vector2 v = target.velocity;
            GUI.Label(new Rect(screenPosition.x, screenPosition.y, 600f, fontSize * 2.5f),
                $"X: {v.x:F2}    Y: {v.y:F2}", _style);
        }
    }
}
