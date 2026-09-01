using System.Collections.Generic;
using SuperQQ.Audio;
using SuperQQ.Microphone;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.GameFlow
{
    /// <summary>
    /// 正式游玩阶段。
    /// 全员出局条件成立后，结算本轮得分并进入配置的下一阶段。
    /// 转移配置建议：[0] 全员出局条件 -> 回合结算阶段。
    /// </summary>
    [CreateAssetMenu(menuName = "SuperQQ/Game Flow/Phases/Playing Phase")]
    public class PlayingPhase : GamePhaseBase
    {
        [Header("音效")]
        [Tooltip("进入正式游玩阶段时播放的开始音效（Clip 在 AudioCatalog 资产中按 Id 拖配）；None 表示静默")]
        [SerializeField] private SfxId _roundStartSfx = SfxId.RoundStart;

        [Tooltip("离开正式游玩阶段时播放的结束音效（Clip 在 AudioCatalog 资产中按 Id 拖配）；None 表示静默")]
        [SerializeField] private SfxId _roundFinishSfx = SfxId.RoundFinish;

        [Header("开局保护")]
        [Tooltip("进入正式游玩阶段后，本地玩家的无敌与禁输入时长（秒）；0 表示不启用")]
        [SerializeField] private float _initialProtectionDuration = 3f;

        [Header("按键放弃")]
        [Tooltip("长按该键达到放弃时长后，本地存活玩家立即进入死亡状态；None 表示禁用")]
        [SerializeField] private KeyCode _giveUpKey = KeyCode.B;

        [Tooltip("触发放弃所需的长按时长（秒）；松开按键即重置进度")]
        [SerializeField] private float _giveUpHoldDuration = 3f;

        private bool _bRoundSettled;

        // ---------- 按键放弃 ----------
        private float _giveUpHoldTimer;             // 当前长按累计时间（秒）

        // ---------- 开局保护（无敌 + 禁输入） ----------
        private bool _bProtectionStarted;           // 本轮是否已施加保护（防重，场景异步加载时由 RefreshSceneRuntimeBindings 补触发）
        private bool _bProtectionActive;            // 保护是否生效中（计时结束或阶段退出时解除）
        private float _protectionTimer;             // 保护剩余时间
        private readonly Dictionary<PlayerController, IPlayerInput> _protectionOriginalInputs = new();  // 被保护玩家的原输入源，解除时还原

        public override void OnEnter(GamePhaseContext context)
        {
            base.OnEnter(context);
            _bRoundSettled = false;
            _bProtectionStarted = false;
            _bProtectionActive = false;
            _protectionOriginalInputs.Clear();
            _giveUpHoldTimer = 0f;

            // 阶段开始音效（2D 全局，走 SFX 总线）
            if (_roundStartSfx != SfxId.None)
            {
                AudioManager.PlaySfx(_roundStartSfx);
            }

            // 兜底复活：正常路径下玩家已在选择阶段开始时复活（PropSelectionPhase.OnEnter），
            // 此处为幂等二次调用（已存活为空操作），覆盖联机迟到/单机独立进入游玩阶段等路径。
            LevelPlayerRegistry.Instance?.ReviveLocalPlayersForNewRound();

            // 施加开局保护（无敌 + 禁输入）；场景异步加载时注册表未就绪，由 RefreshSceneRuntimeBindings 补触发
            TryBeginInitialProtection();

            // 进入游玩阶段开始接收本地玩家麦克风输入，实时检测分贝
            MicVolumeManager.EnsureExists().StartMic();
        }

        public override void OnUpdate(GamePhaseContext context, float deltaTime)
        {
            base.OnUpdate(context, deltaTime);

            UpdateGiveUp(deltaTime);

            if (!_bProtectionActive)
            {
                return;
            }

            _protectionTimer -= deltaTime;
            if (_protectionTimer <= 0f)
            {
                EndInitialProtection();
            }
        }

        /// <summary>
        /// 按键放弃：长按指定键累计计时，达到时长后让本地存活玩家强制死亡；松开按键即重置进度。
        /// 直接读取键盘输入，不受开局保护的空输入屏蔽影响（放弃属于玩家主动行为）。
        /// </summary>
        private void UpdateGiveUp(float deltaTime)
        {
            if (_giveUpKey == KeyCode.None || _giveUpHoldDuration <= 0f)
            {
                return;
            }

            if (Input.GetKey(_giveUpKey))
            {
                _giveUpHoldTimer += deltaTime;
                if (_giveUpHoldTimer >= _giveUpHoldDuration)
                {
                    _giveUpHoldTimer = 0f;
                    GiveUpLocalPlayer();
                }
            }
            else
            {
                _giveUpHoldTimer = 0f;
            }
        }

        /// <summary>
        /// 让本端主控的本地玩家进入死亡状态（主动放弃），不影响其他本地/远程玩家。
        /// 主控玩家取自 LevelPlayerRegistry.FindLocalPlayerObject：联机下即本端唯一本地化身，
        /// 单机多人共用键盘时为首个本地玩家（P1）。
        /// 视为不可豁免死亡：无视无敌状态，幽灵在死亡位置重生；
        /// 死亡/幽灵/通关/冻结中的玩家跳过（冻结视为无法操作）。
        /// </summary>
        private void GiveUpLocalPlayer()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return;
            }

            PlayerController player = registry.FindLocalPlayerObject();
            if (player == null || !player.BIsLocal)
            {
                return;
            }
            if (player.BIsDead || player.BIsGhost || player.BIsFinished || player.BIsFrozen)
            {
                return;
            }

            player.PlayerForceDie(playHitSfx: false, fellOutOfBounds: false);
        }

        public override void RefreshSceneRuntimeBindings(GamePhaseContext context)
        {
            base.RefreshSceneRuntimeBindings(context);

            // 场景异步加载完成后玩家注册表才存在，此处补施加保护（由 _bProtectionStarted 保证不重复）
            TryBeginInitialProtection();
        }

        public override void OnExit(GamePhaseContext context)
        {
            base.OnExit(context);
            _bRoundSettled = false;
            _giveUpHoldTimer = 0f;

            // 阶段提前结束（全员出局等）时兜底解除保护，避免无敌计数与空输入残留到下一阶段
            EndInitialProtection();

            // 阶段结束音效（2D 全局，走 SFX 总线）
            if (_roundFinishSfx != SfxId.None)
            {
                AudioManager.PlaySfx(_roundFinishSfx);
            }

            // 离开游玩阶段停止麦克风输入接收
            if (MicVolumeManager.Instance != null)
            {
                MicVolumeManager.Instance.StopMic();
            }
        }

        /// <summary>
        /// 尝试施加开局保护：为所有本地玩家加无敌计数，并将输入源替换为空输入（屏蔽操作）。
        /// 注册表尚未就绪（场景仍在异步加载）时不置标记，等待 RefreshSceneRuntimeBindings 重试。
        /// </summary>
        private void TryBeginInitialProtection()
        {
            if (_bProtectionStarted || _initialProtectionDuration <= 0f)
            {
                return;
            }

            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null)
            {
                return;
            }

            _bProtectionStarted = true;
            _protectionTimer = _initialProtectionDuration;

            IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                // 只保护本地玩家：联机下远端化身由各端自行保护（RemotePlayerSync 已禁用其本地状态机）
                if (player == null || !player.BIsLocal)
                {
                    continue;
                }

                player.AddInvincibility();
                _protectionOriginalInputs[player] = player.InputSource;
                player.SetInputSource(NullPlayerInput.Instance);
            }

            _bProtectionActive = _protectionOriginalInputs.Count > 0;
        }

        /// <summary>
        /// 解除开局保护：逐玩家移除无敌计数并还原输入源。
        /// 仅在输入源仍为本阶段注入的空输入时还原，避免覆盖保护期内其他系统替换的输入源。
        /// </summary>
        private void EndInitialProtection()
        {
            if (!_bProtectionActive)
            {
                return;
            }
            _bProtectionActive = false;

            foreach (KeyValuePair<PlayerController, IPlayerInput> pair in _protectionOriginalInputs)
            {
                PlayerController player = pair.Key;
                if (player == null)
                {
                    continue;
                }

                player.RemoveInvincibility();
                if (player.InputSource == NullPlayerInput.Instance)
                {
                    player.SetInputSource(pair.Value);
                }
            }
            _protectionOriginalInputs.Clear();
        }

        /// <summary>
        /// 转移选中时结算本轮得分（防重）。
        /// </summary>
        protected override void OnTransitionSelected(GamePhaseContext context, GamePhaseTransition transition)
        {
            if (_bRoundSettled)
            {
                return;
            }

            if (context.ScoreManager != null)
            {
                context.ScoreManager.SettleCurrentRound();
            }
            else
            {
                Debug.LogWarning("[PlayingPhase] PlayerScoreManager 不存在，本轮得分不会被结算。");
            }

            _bRoundSettled = true;
        }
    }
}
