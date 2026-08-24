using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 咒语效果上下文 — 纯 C# 对象，由事件（MagicCircleModifier）在命中咒语时构造并注入
    /// 向效果暴露其所需的运行时引用，效果自身不反向查找场景对象
    /// </summary>
    public class SpellEffectContext
    {
        /// <summary>效果目标玩家（触发本次语音识别的本地玩家）</summary>
        public PlayerController Target { get; }

        /// <summary>协程宿主（来自 LevelEventContext，效果实例经它起计时协程）</summary>
        public MonoBehaviour CoroutineRunner { get; }

        /// <summary>场景根节点（可为空，需要场景级实例的效果使用）</summary>
        public Transform SceneRoot { get; }

        public SpellEffectContext(PlayerController target, MonoBehaviour coroutineRunner, Transform sceneRoot)
        {
            Target = target;
            CoroutineRunner = coroutineRunner;
            SceneRoot = sceneRoot;
        }
    }
}
