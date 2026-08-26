using Minigame.Room.V1;

namespace SuperQQ.Event
{
    /// <summary>
    /// 服务端驱动的随机事件参数接收接口。
    /// 由联机随机事件的 Modifier 实现（如小蛋糕陨石）：
    /// 事件触发后服务端随 RoomSnapshot 持续下发事件参数（event_params1），
    /// LevelEventAnnouncer 收到后路由给本轮选中事件中实现本接口的 Modifier。
    /// 快照全量重复下发，实现方需自行保证幂等（首包生成、后续包增量校验）。
    /// </summary>
    public interface IServerDrivenRandomEvent
    {
        /// <summary>应用服务端下发的事件参数（每次快照到达都可能调用）</summary>
        void ApplyServerEventParams(RandomEventParams eventParams);
    }

    /// <summary>
    /// 服务端驱动的随机事件2参数接收接口（冰冻事件）。
    /// 由冰冻事件的 Modifier 实现：事件触发后服务端随 RoomSnapshot 持续下发
    /// event_params2（冰冻持续时间），LevelEventAnnouncer 路由给实现方。
    /// 快照全量重复下发，实现方需自行保证幂等。
    /// </summary>
    public interface IServerDrivenRandomEvent2
    {
        /// <summary>应用服务端下发的事件参数（每次快照到达都可能调用）</summary>
        void ApplyServerEventParams(RandomEventParams2 eventParams);
    }

    /// <summary>
    /// 服务端驱动的随机事件3状态接收接口（言出法随）。
    /// 由言出法随事件的 Modifier 实现：事件期间服务端随 RoomSnapshot 持续下发
    /// event3_states（player_id -> Event3PlayerState：子类型/剩余时间/检测声音/劈/音量超标玩家列表），
    /// LevelEventAnnouncer 路由给实现方。快照全量重复下发，实现方需自行保证幂等（边沿触发/去重）。
    /// 状态 map 由多变空时会以空 map 补发一次，供实现方做清理。
    /// </summary>
    public interface IServerDrivenRandomEvent3
    {
        /// <summary>应用服务端下发的事件3玩家状态（每次快照到达都可能调用）</summary>
        void ApplyServerEvent3States(System.Collections.Generic.IDictionary<string, Event3PlayerState> states);
    }
}
