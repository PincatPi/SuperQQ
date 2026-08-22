namespace SuperQQ.UI
{
    /// <summary>
    /// 提示（Tips）视图 — 挂在 Tips Prefab 的根节点上
    /// 与弹窗（PopupView）的区别：Tips 只能自动关闭，PopupManager.ShowTips 强制要求有效时长
    /// 当前无额外行为，作为类型标识与后续 Tips 专属能力（如多条堆叠布局、入场动效）的扩展点
    /// </summary>
    public class TipsView : PopupView
    {
    }
}
