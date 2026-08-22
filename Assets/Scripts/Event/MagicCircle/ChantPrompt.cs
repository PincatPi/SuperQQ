using TMPro;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 吟唱提示框 — 挂在言出法随事件提示 Text 框 Prefab 的根节点上
    /// 提示框为屏幕空间 UI（TextMeshProUGUI），由事件实例化到主 Canvas 下，
    /// 在 LateUpdate 中将跟随目标（法阵）的世界坐标加偏移转为屏幕坐标显示
    /// （目标固定不动时屏幕位置仍随相机移动实时换算，与 PlayerNameLabel 同一套跟随逻辑）
    /// 对外暴露 Initialize 与 SetText，由事件逻辑驱动；自身不含任何事件逻辑
    /// </summary>
    public class ChantPrompt : MonoBehaviour
    {
        [Tooltip("显示提示文字的 TMP 组件")]
        [SerializeField] private TMP_Text _text;

        private Camera _camera;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _rectTransform;
        private Transform _target;
        private Vector2 _worldOffset;

        /// <summary>
        /// 初始化提示框：绑定相机、Canvas 与跟随目标
        /// </summary>
        /// <param name="camera">世界坐标转屏幕坐标所用相机</param>
        /// <param name="canvasRect">所在 Canvas 的 RectTransform（本提示框需已实例化为其子节点）</param>
        /// <param name="target">跟随目标（法阵 Transform）</param>
        /// <param name="worldOffset">相对目标的世界坐标偏移</param>
        public void Initialize(Camera camera, RectTransform canvasRect, Transform target, Vector2 worldOffset)
        {
            _camera = camera;
            _canvasRect = canvasRect;
            _canvas = canvasRect != null ? canvasRect.GetComponent<Canvas>() : null;
            _target = target;
            _worldOffset = worldOffset;
        }

        /// <summary>
        /// 设置提示文字内容
        /// </summary>
        /// <param name="content">提示文字</param>
        public void SetText(string content)
        {
            if (_text != null)
            {
                _text.text = content;
            }
        }

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            if (_rectTransform == null)
            {
                // 根节点为普通 Transform 时补齐 RectTransform（提示框实例化在 Canvas 下，必须使用 RectTransform）
                _rectTransform = gameObject.AddComponent<RectTransform>();
            }

            // 锚点/轴心强制居中：anchoredPosition 的参考系由锚点决定，
            // 居中后每帧写入的屏幕换算结果才与 Prefab 锚点配置无关
            _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rectTransform.pivot = new Vector2(0.5f, 0.5f);

            // 文本组件未显式配置时回退查找子级（如文本挂在子物体上）
            if (_text == null)
            {
                _text = GetComponentInChildren<TMP_Text>();
            }
            if (_text == null)
            {
                Debug.LogWarning("[ChantPrompt] 未找到 TMP 文本组件，SetText 将不生效。", this);
            }
        }

        private void LateUpdate()
        {
            // 目标已销毁（玩家化身被移除）时隐藏自身，销毁由事件侧统一管理
            if (_target == null || _camera == null || _canvasRect == null || _rectTransform == null)
            {
                return;
            }

            UpdatePosition();
        }

        /// <summary>
        /// 将目标头顶的世界坐标转换为 Canvas 本地坐标，更新提示框位置
        /// 使用 LateUpdate 确保在所有逻辑和物理更新完成后执行，避免抖动
        /// </summary>
        private void UpdatePosition()
        {
            Vector3 worldPos = _target.position + (Vector3)_worldOffset;
            Vector2 screenPos = _camera.WorldToScreenPoint(worldPos);

            // 根据 Canvas 渲染模式选择正确的 eventCamera
            Camera eventCamera = GetEventCamera();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screenPos, eventCamera, out Vector2 localPos);

            _rectTransform.anchoredPosition = localPos;
        }

        /// <summary>
        /// 根据 Canvas 渲染模式获取事件相机
        /// Screen Space Overlay 传 null；Screen Space Camera / World Space 传 canvas.worldCamera 或主相机
        /// </summary>
        private Camera GetEventCamera()
        {
            if (_canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }
            return _canvas.worldCamera != null ? _canvas.worldCamera : _camera;
        }
    }
}
