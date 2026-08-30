using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 咒语清单按键控制器 — 挂在关卡内 spellBtn 所在物体上
    ///
    /// 功能：
    ///   1. 本地玩家进入咒语法阵时，按键外圈高光 Image 循环闪烁（渐显到满透明度后渐隐，循环）；
    ///      离开全部法阵后停止并隐藏高光。法阵为运行时实例化，无法 Inspector 绑定，
    ///      由法阵事件侧（MagicCircleModifier）经 <see cref="NotifyLocalPlayerEnteredCircle"/> /
    ///      <see cref="NotifyLocalPlayerExitedCircle"/> 静态接口驱动；
    ///   2. 点击按键切换咒语清单面板 SpellList 的显隐（初始强制隐藏，再点一次关闭）。
    ///
    /// 配置：拖入 spellBtn（Button）、外圈高光（Image）、咒语清单面板（GameObject）。
    /// </summary>
    public class SpellListButtonController : MonoBehaviour
    {
        /// <summary>场景内的按键控制器实例（供运行时实例化的法阵事件侧调用）</summary>
        public static SpellListButtonController Instance { get; private set; }

        [Header("UI 绑定")]
        [Tooltip("咒语清单按键；留空时取本物体上的 Button")]
        [SerializeField] private Button _spellButton;
        [Tooltip("按键外圈高光 Image（循环闪烁的对象）")]
        [SerializeField] private Image _highlightImage;
        [Tooltip("咒语清单面板（场景中已做好的 SpellList 预制体实例，初始会被强制隐藏）")]
        [SerializeField] private GameObject _spellListPanel;

        [Header("闪烁参数")]
        [Tooltip("闪烁频率（每秒完整渐显→渐隐的周期数）")]
        [SerializeField, Min(0.05f)] private float _blinkFrequency = 0.8f;
        [Tooltip("高光最大透明度")]
        [SerializeField, Range(0f, 1f)] private float _highlightMaxAlpha = 1f;

        private int _insideCircleCount;     // 本地玩家当前处于内部的法阵数量
        private float _blinkPhase;          // 闪烁相位（0~1 为一个完整周期）

        // ==================== 法阵进出通知（供 MagicCircleModifier 调用） ====================

        /// <summary>本地玩家进入一个法阵：开始/维持高光闪烁（多个法阵并存时按计数管理）</summary>
        public static void NotifyLocalPlayerEnteredCircle()
        {
            if (Instance != null)
            {
                Instance._insideCircleCount++;
            }
        }

        /// <summary>本地玩家离开一个法阵：离开全部法阵后停止闪烁并隐藏高光</summary>
        public static void NotifyLocalPlayerExitedCircle()
        {
            if (Instance == null)
            {
                return;
            }

            Instance._insideCircleCount = Mathf.Max(0, Instance._insideCircleCount - 1);
            if (Instance._insideCircleCount == 0)
            {
                Instance._blinkPhase = 0f;
                Instance.SetHighlightAlpha(0f);
            }
        }

        /// <summary>强制复位高光（法阵销毁/事件停用等异常路径兜底）</summary>
        public static void ResetCircleHighlight()
        {
            if (Instance == null)
            {
                return;
            }
            Instance._insideCircleCount = 0;
            Instance._blinkPhase = 0f;
            Instance.SetHighlightAlpha(0f);
        }

        // ==================== 生命周期 ====================

        private void Awake()
        {
            // 场景中只允许一个实例
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SpellListButton] 场景中存在多个 SpellListButtonController，已销毁重复实例。", this);
                Destroy(this);
                return;
            }
            Instance = this;

            if (_spellButton == null)
            {
                _spellButton = GetComponent<Button>();
            }
            if (_spellButton != null)
            {
                _spellButton.onClick.AddListener(ToggleSpellList);
            }

            // 初始状态：高光隐藏、清单面板关闭
            SetHighlightAlpha(0f);
            if (_spellListPanel != null)
            {
                _spellListPanel.SetActive(false);
            }
        }

        private void OnDisable()
        {
            _insideCircleCount = 0;
            _blinkPhase = 0f;
            SetHighlightAlpha(0f);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (_spellButton != null)
            {
                _spellButton.onClick.RemoveListener(ToggleSpellList);
            }
        }

        private void Update()
        {
            if (_highlightImage == null || _insideCircleCount <= 0)
            {
                return;
            }

            // 余弦波形：相位 0→0.5 渐显到满透明度，0.5→1 渐隐回 0，循环（平滑无跳变）
            _blinkPhase = (_blinkPhase + Time.unscaledDeltaTime * _blinkFrequency) % 1f;
            float alpha = _highlightMaxAlpha * (0.5f - 0.5f * Mathf.Cos(_blinkPhase * Mathf.PI * 2f));
            SetHighlightAlpha(alpha);
        }

        // ==================== 清单面板开关 ====================

        /// <summary>点击按键：切换咒语清单面板显隐</summary>
        private void ToggleSpellList()
        {
            if (_spellListPanel != null)
            {
                _spellListPanel.SetActive(!_spellListPanel.activeSelf);
            }
        }

        // ==================== 高光透明度 ====================

        private void SetHighlightAlpha(float alpha)
        {
            if (_highlightImage == null)
            {
                return;
            }
            Color color = _highlightImage.color;
            color.a = Mathf.Clamp01(alpha);
            _highlightImage.color = color;
        }
    }
}
