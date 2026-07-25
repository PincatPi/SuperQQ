using System.Collections.Generic;
using TMPro;
using UnityEngine;
using SuperQQ.Player;

namespace SuperQQ.UI
{
    /// <summary>
    /// 玩家名称标签管理器 — 在主 UI Canvas 中统一创建和管理所有玩家名称标签
    /// 2-4 人共用一个 Canvas，所有 TextMeshProUGUI 合批为 1 Draw Call
    /// 挂载到主 Canvas（Screen Space Overlay）下的任意 GameObject 上
    /// </summary>
    public class PlayerNameLabelManager : MonoBehaviour
    {
        public static PlayerNameLabelManager Instance { get; private set; }

        [Header("标签样式")]
        [SerializeField] private float _fontSize = 18f;                                // 名称字号
        [SerializeField] private Vector2 _labelSize = new Vector2(120f, 30f);          // 标签尺寸
        [SerializeField] private float _outlineWidth = 0.1f;                           // 描边宽度（0~1）
        [SerializeField] private Color _outlineColor = Color.black;                    // 描边颜色

        private readonly Dictionary<PlayerController, PlayerNameLabel> _labels = new();
        private Camera _camera;
        private RectTransform _canvasRect;

        private void Awake()
        {
            // 场景中只允许一个实例
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _camera = Camera.main;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                _canvasRect = canvas.GetComponent<RectTransform>();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 注册玩家：创建对应的名称标签
        /// 由 PlayerController.Start 自动调用
        /// </summary>
        public void RegisterPlayer(PlayerController player)
        {
            if (player == null || _labels.ContainsKey(player))
            {
                return;
            }

            PlayerNameLabel label = CreateLabel(player);
            _labels[player] = label;
        }

        /// <summary>
        /// 注销玩家：销毁对应的名称标签
        /// 由 PlayerController.OnDestroy 自动调用
        /// </summary>
        public void UnregisterPlayer(PlayerController player)
        {
            if (player == null || !_labels.ContainsKey(player))
            {
                return;
            }

            if (_labels.TryGetValue(player, out PlayerNameLabel label) && label != null)
            {
                Destroy(label.gameObject);
            }
            _labels.Remove(player);
        }

        /// <summary>
        /// 创建标签 GameObject，配置 TextMeshProUGUI 和 PlayerNameLabel 组件
        /// 所有标签共享同一 Canvas，TMP 可合批为单次 Draw Call
        /// </summary>
        private PlayerNameLabel CreateLabel(PlayerController player)
        {
            GameObject obj = new GameObject($"Label_{player.PlayerName}");
            obj.transform.SetParent(transform, false);

            // 配置 RectTransform
            RectTransform rectTransform = obj.AddComponent<RectTransform>();
            rectTransform.sizeDelta = _labelSize;

            // 配置 TextMeshProUGUI
            TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = _fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false; // 不接收射线检测，节省开销
            // 通过 fontMaterial 访问器创建材质实例，直接设置 Shader 属性确保描边生效
            tmp.fontMaterial.SetFloat("_OutlineWidth", _outlineWidth);
            tmp.fontMaterial.SetColor("_OutlineColor", _outlineColor);

            // 添加 PlayerNameLabel 组件并初始化
            PlayerNameLabel label = obj.AddComponent<PlayerNameLabel>();
            label.Initialize(_camera, _canvasRect, player);

            return label;
        }
    }
}
