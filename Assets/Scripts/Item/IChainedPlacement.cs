using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 衔接摆放契约 — 放置确认后需要接连摆放下一件的道具实现（如传送门：入口确认后衔接出口）
    /// 该能力不具备全体道具的通用性，故不作为 ItemBase 基类成员，按需实现本接口即可
    ///
    /// 职责划分：实例生成由道具负责（它知道 prefab 与出生偏移），
    /// 摆放交互（拖拽/跟随/确认/取消）由调用方的摆放流程负责，本接口方法不做任何输入状态切换
    /// </summary>
    public interface IChainedPlacement
    {
        /// <summary>生成需要衔接摆放的下一件道具实例；无衔接需求（或条件未满足）返回 null</summary>
        GameObject SpawnChainedItem();
    }
}
