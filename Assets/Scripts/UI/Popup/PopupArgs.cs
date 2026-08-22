using System;
using UnityEngine;

namespace SuperQQ.UI
{
    /// <summary>
    /// 弹窗播放参数 — 由调用方构造，经 PopupManager 传递给 PopupView.OnShow 应用
    /// 所有字段均可选：保留默认值时 Prefab 上的原始内容不变
    /// 具体弹窗如需更多参数，可继承本类扩展字段，并在 PopupView 子类的 OnShow 中解析
    /// （Tips 不使用本类：结构简单，ShowTips 仅接收文本与时长）
    /// </summary>
    public class PopupArgs
    {
        /// <summary>标题文本；为 null 或空时保留 Prefab 原文</summary>
        public string Title;

        /// <summary>正文文本；为 null 或空时保留 Prefab 原文</summary>
        public string Content;

        /// <summary>图片；为 null 时保留 Prefab 原图</summary>
        public Sprite Image;

        /// <summary>
        /// 自动关闭时长（秒）：
        /// 负数 = 使用 PopupManager 注册表中该类型的默认时长；
        /// 0 = 不自动关闭，需 Prefab 上的关闭按钮或外部 ClosePopup 关闭；
        /// 正数 = 指定时长后自动关闭
        /// </summary>
        public float Duration = -1f;

        /// <summary>关闭完成回调（自动关闭与手动关闭均触发），可为空</summary>
        public Action OnClosed;

        /// <summary>快捷构造：仅指定自动关闭时长</summary>
        public static PopupArgs WithDuration(float duration)
        {
            return new PopupArgs { Duration = duration };
        }

        /// <summary>快捷构造：仅指定正文文本（可选同时指定时长）</summary>
        public static PopupArgs WithContent(string content, float duration = -1f)
        {
            return new PopupArgs { Content = content, Duration = duration };
        }
    }
}
