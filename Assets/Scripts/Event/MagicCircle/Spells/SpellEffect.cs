using SuperQQ.Audio;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 咒语效果抽象基类 — ScriptableObject 资产，纯配置载体（预制体/时长等参数），不持有运行时状态
    /// 契约：Activate(SpellEffectContext) 创建并返回一个运行时效果实例；
    /// 效果激活成功（返回非空实例）时统一播放咒语生效音效（全咒语共用，子类无需关心）
    /// 新增效果（护盾/加速/变大等）= 继承本类实现 OnActivate，策划侧纯配置扩展
    /// </summary>
    public abstract class SpellEffect : ScriptableObject
    {
        [Header("音效")]
        [Tooltip("咒语生效音效：效果激活成功时在目标玩家位置 3D 播放，全咒语共用（Clip 在 AudioCatalog 资产中按 Id 拖配）；None 表示静默")]
        [SerializeField] private SfxId _castSfx = SfxId.SpellCast;

        /// <summary>
        /// 激活效果（模板方法，密封）：委托子类创建运行时实例，成功后统一播放生效音效。
        /// 任何触发路径（法阵语音、测试热键、网络回放等）行为一致
        /// </summary>
        /// <param name="context">效果上下文（触发玩家、协程宿主等运行时引用）</param>
        /// <returns>运行时效果实例；上下文无效（如触发玩家缺失）时返回 null</returns>
        public SpellEffectInstance Activate(SpellEffectContext context)
        {
            SpellEffectInstance instance = OnActivate(context);
            if (instance != null && context != null && context.Target != null)
            {
                PlayCastSfx(context.Target.transform.position);
            }
            return instance;
        }

        /// <summary>
        /// 子类实现：按自身配置创建运行时效果实例并使其生效（不播音效，由基类统一处理）
        /// </summary>
        protected abstract SpellEffectInstance OnActivate(SpellEffectContext context);

        /// <summary>
        /// 播放生效音效（服务端驱动等无运行时实例的模式下由子类手动调用）
        /// </summary>
        protected void PlayCastSfx(UnityEngine.Vector3 position)
        {
            if (_castSfx != SfxId.None)
            {
                AudioManager.PlaySfxAt(_castSfx, position);
            }
        }

        /// <summary>
        /// 联机：应用服务端下发的事件3玩家状态（每次快照到达都可能调用，全量重复下发）。
        /// 基类空实现；需要服务端同步的效果（如雷公助我）重写，内部自行保证幂等（边沿触发/去重）
        /// </summary>
        /// <param name="states">player_id -> Event3PlayerState（子类型/剩余时间/检测声音/劈/音量超标玩家列表）</param>
        /// <param name="eventContext">事件运行时上下文（协程宿主/场景根节点）</param>
        public virtual void ApplyServerEvent3States(
            System.Collections.Generic.IDictionary<string, Minigame.Room.V1.Event3PlayerState> states,
            LevelEventContext eventContext)
        {
        }

        /// <summary>
        /// 联机：事件结束时清理服务端同步产生的运行时表现。基类空实现
        /// </summary>
        public virtual void EndServerDrivenEffects()
        {
        }

        /// <summary>
        /// 服务端驱动的远端咒语特效同步器（言出法随联机用）：
        /// 为 event3_states 中 subtype 匹配的【远端】玩家挂载特效（本地玩家的特效由本地实例管理，跳过）；
        /// 条目消失 / subtype 被服务端重置 / 剩余时间归 0 / 超本地配置时长（兜底）时移除销毁。
        /// 快照全量重复下发，内部幂等（重复 Apply 不重复挂载）
        /// </summary>
        protected sealed class RemoteSpellFxSync
        {
            private readonly GameObject _prefab;
            private readonly UnityEngine.Vector2 _offset;
            private readonly int _subtype;
            private readonly float _maxDuration;

            // playerId -> 已挂载的特效 / 挂载时刻
            private readonly System.Collections.Generic.Dictionary<string, GameObject> _fxByPlayer = new();
            private readonly System.Collections.Generic.Dictionary<string, float> _attachTimeByPlayer = new();

            // 见过正剩余时间的 playerId：remaining_ms 归 0 后恒 0，须见过正值才能以"归 0"判定效果结束（防起始瞬间误判）
            private readonly System.Collections.Generic.HashSet<string> _seenPositiveRemaining = new();

            // 移除用的临时缓存（避免遍历时修改集合）
            private readonly System.Collections.Generic.List<string> _removeCache = new();

            public RemoteSpellFxSync(GameObject prefab, UnityEngine.Vector2 offset, int subtype, float maxDuration)
            {
                _prefab = prefab;
                _offset = offset;
                _subtype = subtype;
                _maxDuration = maxDuration;
            }

            public void Apply(System.Collections.Generic.IDictionary<string, Minigame.Room.V1.Event3PlayerState> states)
            {
                // 清理不再匹配本咒语的残留记录（条目消失/subtype 重置后，重新施法按新效果重新判定）
                if (_seenPositiveRemaining.Count > 0)
                {
                    _removeCache.Clear();
                    foreach (string id in _seenPositiveRemaining)
                    {
                        if (!states.TryGetValue(id, out Minigame.Room.V1.Event3PlayerState s)
                            || s == null || s.Subtype != _subtype)
                        {
                            _removeCache.Add(id);
                        }
                    }
                    for (int i = 0; i < _removeCache.Count; i++)
                    {
                        _seenPositiveRemaining.Remove(_removeCache[i]);
                    }
                }

                // 记录正的剩余时间
                foreach (System.Collections.Generic.KeyValuePair<string, Minigame.Room.V1.Event3PlayerState> pair in states)
                {
                    Minigame.Room.V1.Event3PlayerState state = pair.Value;
                    if (state != null && state.Subtype == _subtype && state.RemainingMs > 0)
                    {
                        _seenPositiveRemaining.Add(pair.Key);
                    }
                }

                // 移除：条目消失 / subtype 重置 / 剩余时间归 0 / 超本地时长兜底
                if (_fxByPlayer.Count > 0)
                {
                    _removeCache.Clear();
                    foreach (System.Collections.Generic.KeyValuePair<string, GameObject> pair in _fxByPlayer)
                    {
                        bool bRemove;
                        if (!states.TryGetValue(pair.Key, out Minigame.Room.V1.Event3PlayerState state)
                            || state == null || state.Subtype != _subtype)
                        {
                            bRemove = true; // 条目消失或服务端重置 subtype（效果结束）
                        }
                        else if (_seenPositiveRemaining.Contains(pair.Key) && state.RemainingMs <= 0)
                        {
                            bRemove = true; // 剩余时间归 0（服务端计时结束）
                        }
                        else
                        {
                            bRemove = Time.time - _attachTimeByPlayer[pair.Key] > _maxDuration + 1f; // 本地时长兜底（+1s 宽限）
                        }

                        if (bRemove)
                        {
                            _removeCache.Add(pair.Key);
                        }
                    }
                    for (int i = 0; i < _removeCache.Count; i++)
                    {
                        string id = _removeCache[i];
                        if (_fxByPlayer[id] != null)
                        {
                            Object.Destroy(_fxByPlayer[id]);
                        }
                        _fxByPlayer.Remove(id);
                        _attachTimeByPlayer.Remove(id);
                        _seenPositiveRemaining.Remove(id);
                    }
                }

                if (_prefab == null)
                {
                    return;
                }

                SuperQQ.Network.NetworkManager net = SuperQQ.Network.NetworkManager.Instance;
                string localPlayerId = net != null ? net.LocalPlayerId : null;
                SuperQQ.Player.LevelPlayerRegistry registry = SuperQQ.Player.LevelPlayerRegistry.Instance;
                if (registry == null)
                {
                    return;
                }

                // 挂载：subtype 匹配的远端玩家（本地玩家由本地实例管理；剩余时间已归 0 的结束条目不挂；
                // 化身尚未生成时本帧跳过，下一帧快照重试）
                foreach (System.Collections.Generic.KeyValuePair<string, Minigame.Room.V1.Event3PlayerState> pair in states)
                {
                    Minigame.Room.V1.Event3PlayerState state = pair.Value;
                    if (state == null || state.Subtype != _subtype || _fxByPlayer.ContainsKey(pair.Key))
                    {
                        continue;
                    }
                    if (pair.Key == localPlayerId)
                    {
                        continue;
                    }
                    if (_seenPositiveRemaining.Contains(pair.Key) && state.RemainingMs <= 0)
                    {
                        continue;
                    }

                    SuperQQ.Player.PlayerController player = FindPlayerById(registry, pair.Key);
                    if (player == null)
                    {
                        continue;
                    }

                    GameObject fx = Object.Instantiate(_prefab, player.transform);
                    fx.transform.localPosition = _offset;
                    _fxByPlayer.Add(pair.Key, fx);
                    _attachTimeByPlayer.Add(pair.Key, Time.time);
                }
            }

            /// <summary>清理全部远端特效（事件 Deactivate 时调用）</summary>
            public void Clear()
            {
                foreach (System.Collections.Generic.KeyValuePair<string, GameObject> pair in _fxByPlayer)
                {
                    if (pair.Value != null)
                    {
                        Object.Destroy(pair.Value);
                    }
                }
                _fxByPlayer.Clear();
                _attachTimeByPlayer.Clear();
                _seenPositiveRemaining.Clear();
            }

            private static SuperQQ.Player.PlayerController FindPlayerById(SuperQQ.Player.LevelPlayerRegistry registry, string playerId)
            {
                System.Collections.Generic.IReadOnlyList<SuperQQ.Player.PlayerController> players = registry.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] != null && players[i].PlayerId == playerId)
                    {
                        return players[i];
                    }
                }
                return null;
            }
        }
    }
}
