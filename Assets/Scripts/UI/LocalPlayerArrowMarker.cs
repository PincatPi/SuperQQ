using SuperQQ.GameFlow;
using SuperQQ.Player;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 本地玩家头顶箭头标记 — 仅在 PlayingPhase（游玩阶段）显示，用于标识"这是我"。
    /// 挂载到主 Canvas（Screen Space Overlay）下的任意 GameObject 上（建议与 PlayerNameLabelManager 同物体），
    /// 运行时自动创建一张 UI Image 并跟随本地玩家（LevelPlayerRegistry 中 BIsLocal 的化身）头顶。
    ///
    /// 阶段显隐：订阅 GamePhaseManager.OnPhaseChanged，进入 PlayingPhase 显示、离开隐藏；
    /// 用 Image.enabled 而非 SetActive，保证隐藏期间脚本仍运行、能收到阶段事件。
    ///
    /// Editor 接线：
    ///   arrowSprite ← 箭头 Sprite（如 Assets/Images/UI/CoronaArrow.png，Texture Type 需为 Sprite）
    /// </summary>
    public class LocalPlayerArrowMarker : MonoBehaviour
    {
        [Header("箭头样式")]
        [SerializeField] private Sprite _arrowSprite;                              // 箭头图片（必配）
        [SerializeField] private Vector2 _arrowSize = new Vector2(48f, 48f);       // 箭头尺寸（像素）
        [SerializeField] private Vector3 _worldOffset = new Vector3(0f, 1.9f, 0f); // 相对玩家脚底的世界偏移（应高于名称标签的 1.2）

        [Header("上下浮动动画（幅度设为 0 关闭）")]
        [SerializeField] private float _bobAmplitude = 10f;                        // 浮动幅度（屏幕像素）
        [SerializeField] private float _bobSpeed = 3f;                             // 浮动速度（周期/秒）

        private RectTransform _arrowRect;
        private Image _arrowImage;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private Camera _camera;
        private PlayerController _target;
        private bool _bPhaseVisible;
        private GamePhaseManager _subscribedManager;

        private void Awake()
        {
            _camera = Camera.main;
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null)
            {
                _canvasRect = _canvas.GetComponent<RectTransform>();
            }
            CreateArrow();
        }

        private void OnEnable()
        {
            TrySubscribePhaseManager();
        }

        private void OnDisable()
        {
            if (_subscribedManager != null)
            {
                _subscribedManager.OnPhaseChanged -= HandlePhaseChanged;
                _subscribedManager = null;
            }
        }

        /// <summary>订阅阶段事件（幂等）：组件激活时 GamePhaseManager 可能尚未就绪，Update 中持续补订阅</summary>
        private void TrySubscribePhaseManager()
        {
            GamePhaseManager manager = GamePhaseManager.Instance;
            if (manager == null || ReferenceEquals(manager, _subscribedManager))
            {
                return;
            }
            if (_subscribedManager != null)
            {
                _subscribedManager.OnPhaseChanged -= HandlePhaseChanged;
            }
            manager.OnPhaseChanged += HandlePhaseChanged;
            _subscribedManager = manager;

            // 补订阅成功时同步一次当前阶段（可能已在 PlayingPhase 中）
            _bPhaseVisible = manager.CurrentPhaseAsset is PlayingPhase;
        }

        private void HandlePhaseChanged(GamePhaseBase previousPhase, GamePhaseBase nextPhase)
        {
            _bPhaseVisible = nextPhase is PlayingPhase;
        }

        private void Update()
        {
            TrySubscribePhaseManager();
            if (_target == null)
            {
                _target = FindLocalPlayer();
            }
            if (_arrowImage != null)
            {
                _arrowImage.enabled = _bPhaseVisible && _target != null && _arrowSprite != null;
            }
        }

        /// <summary>查找本地玩家化身（BIsLocal）；玩家尚未生成时返回 null，下一帧继续找</summary>
        private static PlayerController FindLocalPlayer()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return null;
            }
            System.Collections.Generic.IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].BIsLocal)
                {
                    return players[i];
                }
            }
            return null;
        }

        /// <summary>在 Canvas 下创建箭头 Image（运行时创建，无需场景预制）</summary>
        private void CreateArrow()
        {
            GameObject obj = new GameObject("LocalPlayerArrow");
            obj.transform.SetParent(transform, false);

            _arrowRect = obj.AddComponent<RectTransform>();
            _arrowRect.sizeDelta = _arrowSize;

            _arrowImage = obj.AddComponent<Image>();
            _arrowImage.sprite = _arrowSprite;
            _arrowImage.preserveAspect = true;
            _arrowImage.raycastTarget = false;
            _arrowImage.enabled = false;
        }

        /// <summary>
        /// 将玩家头顶世界坐标转为 Canvas 本地坐标（与 PlayerNameLabel 同一套跟随逻辑），
        /// 叠加正弦浮动动画；LateUpdate 保证在所有移动/物理更新后执行，避免抖动
        /// </summary>
        private void LateUpdate()
        {
            if (_arrowImage == null || !_arrowImage.enabled || _target == null || _canvasRect == null)
            {
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }

            Vector3 worldPos = _target.transform.position + _worldOffset;
            Vector2 screenPos = _camera.WorldToScreenPoint(worldPos);

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? (_canvas.worldCamera != null ? _canvas.worldCamera : _camera)
                : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screenPos, eventCamera, out Vector2 localPos);

            if (_bobAmplitude > 0f)
            {
                localPos.y += Mathf.Sin(Time.time * _bobSpeed * Mathf.PI * 2f) * _bobAmplitude;
            }
            _arrowRect.anchoredPosition = localPos;
        }
    }
}
