using System.Collections.Generic;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Event
{
    /// <summary>
    /// 【临时测试脚本，用完删除】无敌金身效果测试热键
    /// 挂载到场景中任意物体，Inspector 拖入 ShieldSpellEffect 资产后，
    /// 按下测试按键即可跳过 法阵→语音识别→咒语匹配 链路，直接对本地玩家触发护盾效果
    /// </summary>
    public class ShieldEffectTestHotkey : MonoBehaviour
    {
        [Tooltip("测试按键")]
        [SerializeField] private KeyCode _testKey = KeyCode.K;

        [Tooltip("要测试的护盾效果资产（ShieldSpellEffect）")]
        [SerializeField] private SpellEffect _testEffect;

        private void Update()
        {
            if (_testEffect == null || !UnityEngine.Input.GetKeyDown(_testKey))
            {
                return;
            }

            PlayerController localPlayer = FindLocalPlayer();
            if (localPlayer == null)
            {
                Debug.LogWarning("[ShieldEffectTestHotkey] 未找到本地玩家。");
                return;
            }

            // 以自身作为协程宿主构造上下文，直接激活效果
            SpellEffectContext context = new SpellEffectContext(localPlayer, this, null);
            SpellEffectInstance instance = _testEffect.Activate(context);
            Debug.Log($"[ShieldEffectTestHotkey] 触发无敌金身：{(instance != null ? "已生效" : "激活失败")}");
        }

        private static PlayerController FindLocalPlayer()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return null;
            }

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].BIsLocal)
                {
                    return players[i];
                }
            }
            return null;
        }
    }
}
