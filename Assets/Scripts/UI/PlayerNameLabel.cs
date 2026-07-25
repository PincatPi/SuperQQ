using TMPro;
using UnityEngine;
using SuperQQ.Player;

namespace SuperQQ.UI
{
    /// <summary>
    /// 玩家名称标签 — 跟随指定玩家世界位置，在屏幕空间显示玩家名称
    /// 挂载到主 Canvas（Screen Space Overlay）下的 GameObject 上
    /// 在 LateUpdate 中将玩家头顶世界坐标转为屏幕坐标，更新标签位置
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class PlayerNameLabel : MonoBehaviour
    {
        [Header("位置偏移")]
        [SerializeField] private Vector3 _worldOffset = new Vector3(0f, 1.2f, 0f); // 名称在玩家头顶的世界坐标偏移

        [Header("颜色设置")]
        [SerializeField] private Color _ghostColor = new Color(0.6f, 0.6f, 0.6f, 0.7f); // 幽灵状态名称颜色

        private Camera _camera;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _rectTransform;
        private TextMeshProUGUI _text;
        private PlayerController _playerController;
        private Color _aliveColor;       // 存活状态名称颜色，从玩家专属颜色初始化
        private bool _bWasGhost;         // 上一帧是否为幽灵状态，用于检测状态变更

        /// <summary>
        /// 初始化标签：绑定相机、Canvas、目标玩家
        /// 从 PlayerController 读取名称和颜色
        /// </summary>
        public void Initialize(Camera camera, RectTransform canvasRect, PlayerController player)
        {
            _camera = camera;
            _canvasRect = canvasRect;
            _canvas = canvasRect.GetComponent<Canvas>();
            _playerController = player;
            _text.text = player.PlayerName;
            _aliveColor = player.PlayerColor;
            _text.color = _aliveColor;
            _bWasGhost = false;
        }

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _text = GetComponent<TextMeshProUGUI>();
        }

        private void LateUpdate()
        {
            if (_playerController == null)
            {
                gameObject.SetActive(false);
                return;
            }

            UpdatePosition();
            UpdateColorByState();
        }

        /// <summary>
        /// 将玩家头顶的世界坐标转换为屏幕坐标，更新标签位置
        /// 使用 LateUpdate 确保在所有逻辑和物理更新完成后执行，避免抖动
        /// </summary>
        private void UpdatePosition()
        {
            Vector3 worldPos = _playerController.transform.position + _worldOffset;
            Vector2 screenPos = _camera.WorldToScreenPoint(worldPos);

            // 根据Canvas渲染模式选择正确的eventCamera
            Camera eventCamera = GetEventCamera();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screenPos, eventCamera, out Vector2 localPos);

            _rectTransform.anchoredPosition = localPos;
        }

        /// <summary>
        /// 根据Canvas渲染模式获取事件相机
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

        /// <summary>
        /// 根据玩家存活/幽灵状态切换名称颜色
        /// 仅在状态变更时更新，避免每帧设置颜色
        /// </summary>
        private void UpdateColorByState()
        {
            bool bIsGhost = _playerController.BIsDead;
            if (bIsGhost != _bWasGhost)
            {
                _text.color = bIsGhost ? _ghostColor : _aliveColor;
                _bWasGhost = bIsGhost;
            }
        }
    }
}
