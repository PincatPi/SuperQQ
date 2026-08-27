using System;
using System.Collections.Generic;
using SuperQQ.Player;
using UnityEngine;
using UnityEngine.Serialization;

namespace SuperQQ.Event
{
    /// <summary>
    /// 【临时测试脚本，用完删除】咒语效果测试热键
    /// 挂载到场景中任意物体，Inspector 中为每条测试项拖入 SpellEffect 资产并配置按键，
    /// 按下对应按键即可跳过 法阵→语音识别→咒语匹配 链路，直接对本地玩家触发该咒语效果。
    /// 与正常流程零耦合：不参与任何业务逻辑；关闭总开关、禁用组件或直接删除本文件即可彻底关闭，
    /// 正式包不挂此组件则完全无影响
    /// </summary>
    public class SpellEffectTestHotkey : MonoBehaviour
    {
        /// <summary>单条咒语测试项：按键 + 效果资产 + 独立开关</summary>
        [Serializable]
        private class SpellTestEntry
        {
            [Tooltip("测试项名称（仅用于 Inspector 辨识与日志输出）")]
            public string Label = "未命名咒语";

            [Tooltip("该测试项独立开关：取消勾选后对应按键失效")]
            public bool BEnabled = true;

            [Tooltip("测试按键（None 表示不绑定按键）")]
            public KeyCode TestKey = KeyCode.None;

            [Tooltip("要测试的咒语效果资产（SpellEffect 及其子类：护盾/飞行/雷电等）")]
            public SpellEffect TestEffect;
        }

        [Tooltip("测试总开关：关闭后全部测试热键失效（等效于禁用本组件，正常流程不受影响）")]
        [SerializeField] private bool _bEnableTest = true;

        [Tooltip("咒语测试列表：每条 = 按键 + 效果资产，可自由增删改；默认三条对应 无敌金身(K)/中国人能飞(L)/雷公助我(J)")]
        [SerializeField] private List<SpellTestEntry> _testEntries = new List<SpellTestEntry>
        {
            new SpellTestEntry { Label = "无敌金身", TestKey = KeyCode.K },
            new SpellTestEntry { Label = "中国人能飞", TestKey = KeyCode.L },
            new SpellTestEntry { Label = "雷公助我", TestKey = KeyCode.J },
        };

        // ===== 旧版单护盾字段（仅用于兼容场景中已配置过的组件，运行时自动迁移到列表后失效，勿再使用）=====
        [FormerlySerializedAs("_testKey")]
        [SerializeField, HideInInspector] private KeyCode _legacyTestKey = KeyCode.K;

        [FormerlySerializedAs("_testEffect")]
        [SerializeField, HideInInspector] private SpellEffect _legacyTestEffect;

        private void Awake()
        {
            MigrateLegacyConfig();
        }

        private void Update()
        {
            if (!_bEnableTest)
            {
                return;
            }

            for (int i = 0; i < _testEntries.Count; i++)
            {
                SpellTestEntry entry = _testEntries[i];
                if (entry == null || !entry.BEnabled || entry.TestKey == KeyCode.None
                    || !UnityEngine.Input.GetKeyDown(entry.TestKey))
                {
                    continue;
                }

                Trigger(entry);
            }
        }

        /// <summary>对本地玩家触发一条咒语测试</summary>
        private void Trigger(SpellTestEntry entry)
        {
            if (entry.TestEffect == null)
            {
                Debug.LogWarning($"[SpellEffectTestHotkey] 测试项「{entry.Label}」未配置 SpellEffect 资产。");
                return;
            }

            PlayerController localPlayer = FindLocalPlayer();
            if (localPlayer == null)
            {
                Debug.LogWarning("[SpellEffectTestHotkey] 未找到本地玩家。");
                return;
            }

            // 以自身作为协程宿主构造上下文，直接激活效果
            SpellEffectContext context = new SpellEffectContext(localPlayer, this, null);
            SpellEffectInstance instance = entry.TestEffect.Activate(context);
            Debug.Log($"[SpellEffectTestHotkey] 触发「{entry.Label}」：{(instance != null ? "已生效" : "激活失败")}");
        }

        /// <summary>
        /// 旧版字段迁移：场景组件上残留单护盾配置（_testEffect/_testKey）时并入列表并清空旧字段；
        /// 幂等，每次运行至多生效一次，迁移后旧字段不再参与任何逻辑
        /// </summary>
        private void MigrateLegacyConfig()
        {
            if (_legacyTestEffect == null)
            {
                return;
            }

            bool bExists = false;
            for (int i = 0; i < _testEntries.Count; i++)
            {
                if (_testEntries[i] != null && _testEntries[i].TestEffect == _legacyTestEffect)
                {
                    bExists = true;
                    break;
                }
            }

            if (!bExists)
            {
                _testEntries.Insert(0, new SpellTestEntry
                {
                    Label = "无敌金身",
                    TestKey = _legacyTestKey,
                    TestEffect = _legacyTestEffect,
                });
            }

            _legacyTestEffect = null;
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
