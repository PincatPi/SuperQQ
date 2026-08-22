using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 弹窗视图基类 — 挂在弹窗/Tips Prefab 的根节点上
    /// 职责：
    ///   - 持有通用 UI 绑定（标题/正文/图片/关闭按钮，全部可选）
    ///   - OnShow 时应用 PopupArgs 中的参数，OnHide 时供子类清理运行态
    ///   - 关闭按钮点击时仅发起关闭请求，生命周期统一由 PopupManager 决定
    /// 固定文案的简单弹窗直接挂本类即可；需要自定义参数或行为的弹窗继承本类并重写 OnShow/OnHide
    /// 本类不含任何游戏业务逻辑，也不感知 PopupManager 的具体实现
    /// </summary>
    public class PopupView : MonoBehaviour
    {
        [Header("通用绑定（均可选）")]
        [Tooltip("标题文本：PopupArgs.Title 非空时写入")]
        [SerializeField] protected TMP_Text _titleLabel;

        [Tooltip("正文文本：PopupArgs.Content 非空时写入")]
        [SerializeField] protected TMP_Text _contentLabel;

        [Tooltip("图片：PopupArgs.Image 非空时替换")]
        [SerializeField] protected Image _image;

        [Tooltip("关闭按钮：点击后请求关闭本弹窗（手动关闭型弹窗必配，也可改用任意 Button 的 OnClick 绑定 RequestClose）")]
        [SerializeField] private Button _closeButton;

        /// <summary>
        /// 关闭请求事件：由关闭按钮或子类逻辑触发，PopupManager 监听并执行关闭
        /// 视图自身不销毁/隐藏自己，保证生命周期单一入口
        /// </summary>
        public event Action<PopupView> CloseRequested;

        /// <summary>本视图当前是否处于展示中（由 PopupManager 维护，外部只读）</summary>
        public bool BIsShowing { get; internal set; }

        protected virtual void Awake()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(RequestClose);
            }
        }

        protected virtual void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(RequestClose);
            }
        }

        /// <summary>
        /// 展示时调用：应用播放参数。子类重写以解析自定义参数时建议先调用基类
        /// </summary>
        /// <param name="args">播放参数，可为 null（保留 Prefab 原始内容）</param>
        public virtual void OnShow(PopupArgs args)
        {
            if (args == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(args.Title) && _titleLabel != null)
            {
                _titleLabel.text = args.Title;
            }

            if (!string.IsNullOrEmpty(args.Content) && _contentLabel != null)
            {
                _contentLabel.text = args.Content;
            }

            if (args.Image != null && _image != null)
            {
                _image.sprite = args.Image;
            }
        }

        /// <summary>
        /// 关闭销毁前调用：子类可在此清理运行态（进度归零、退订事件等）
        /// 注意本方法之后实例会被 PopupManager 销毁，不应在此自行销毁
        /// </summary>
        public virtual void OnHide()
        {
        }

        /// <summary>
        /// 请求关闭本弹窗：实际关闭由 PopupManager 统一执行
        /// 可直接绑定到任意 Button 的 OnClick（适用于关闭按钮不在 Prefab 默认绑定上的情况）
        /// </summary>
        public void RequestClose()
        {
            CloseRequested?.Invoke(this);
        }
    }
}
