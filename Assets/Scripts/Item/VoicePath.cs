using SuperQQ.Grid;
using SuperQQ.Microphone;
using SuperQQ.Network;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 声控浮桥 — 搭路类道具（5x1，footprint 在 PlacableItemDef 资产中配置）
    /// 记录放置者玩家（Placed.OwnerKey），仅当本端玩家即放置者时，
    /// 在 PlayingPhase 阶段随本地麦克风实时分贝上下平移（仅 PlayingPhase 开麦，见 PlayingPhase/MicVolumeManager）；
    /// 其它玩家放置的浮桥在本端不响应（本端采集不到其分贝，各端只驱动自己放置的浮桥）。
    ///
    /// 平移规则：放置者归一化音量（0~1，当前分贝占总分贝的百分比）超过 riseThreshold 时沿本地 up 升至上限，
    /// 未达到时沿本地 down 落至下限；上下限（maxUpCells / maxDownCells，单位格）均可配置。
    /// 平移方向为自身本地坐标系的上下（摆放旋转档决定：0°=世界上 90°=世界左 180°=世界下 270°=世界右），
    /// 故旋转 90° 摆放后平台在左右方向移动。
    ///
    /// 生命周期：OnRunPhaseStart 开始响应，OnBuildPhaseStart 停止并复位到放置位置。
    /// 移动经 Kinematic Rigidbody2D.MovePosition（沿用 PillarElevatorController 的做法），
    /// 保证站在桥上的玩家被物理系统正确托举（transform 直写会导致穿模掉落）。
    ///
    /// 联机同步：放置者端按 positionReportRate 节流上报位置（NetEventSync.ReportItemPosition
    /// → ItemPositionSync，服务器透传 ItemPositionSyncBroadcast），其它端按广播位置平滑跟随；
    /// 阶段切换复位由各端本地阶段钩子完成，不走网络。
    /// </summary>
    public class VoicePath : ItemBase
    {
        [Header("平移范围（格）")]
        [Tooltip("上移上限：音量满格时相对放置位置最多上升的格数")]
        [SerializeField, Min(0f)] private float maxUpCells = 4f;
        [Tooltip("下移下限：音量归零时相对放置位置最多下降的格数")]
        [SerializeField, Min(0f)] private float maxDownCells = 2f;

        [Header("声音阈值")]
        [Tooltip("上升阈值（0~1，当前分贝占总分贝的百分比）：放置者分贝超过该值时平台升起，未达到时落下")]
        [SerializeField, Range(0f, 1f)] private float riseThreshold = 0.5f;

        [Header("跟随")]
        [Tooltip("朝目标高度移动的速度（格/秒），越大越跟手")]
        [SerializeField, Min(0.1f)] private float moveSpeedCells = 4f;

        [Header("联机同步")]
        [Tooltip("位置上报频率（次/秒）：放置者端按此频率把浮桥位置经服务器广播给其它端")]
        [SerializeField, Range(5f, 30f)] private float positionReportRate = 10f;
        [Tooltip("远端跟随平滑速度：其它端收到位置广播后向目标位置的收敛速度（越大越跟手）")]
        [SerializeField, Min(1f)] private float remoteFollowSpeed = 12f;
        [Tooltip("远端偏差超过该距离（格）直接瞬移（防长时间追不上）")]
        [SerializeField, Min(0.5f)] private float remoteTeleportCells = 5f;

        [Header("调试")]
        [Tooltip("忽略放置者校验，直接响应本端麦克风（单机测试场景用）")]
        [SerializeField] private bool debugIgnoreOwner;

        /// <summary>搭路：可站立的声控平台</summary>
        public override ItemCategory Category => ItemCategory.Path;

        /// <summary>上移上限（格）</summary>
        public float MaxUpCells => maxUpCells;
        /// <summary>下移下限（格）</summary>
        public float MaxDownCells => maxDownCells;
        /// <summary>上升阈值（0~1，当前分贝占总分贝的百分比）</summary>
        public float RiseThreshold => riseThreshold;

        private Rigidbody2D body;
        private Vector3 basePosition;   // 放置位置（平移基准）
        private Vector3 baseUp = Vector3.up; // 放置时的本地 up 方向（随摆放旋转档变化：0°=世界上 90°=世界左 180°=世界下 270°=世界右）
        private bool responding;        // PlayingPhase 中且本端为放置者时置真

        // 联机同步：放置者端节流上报位置；其它端记录远端目标位置并平滑跟随
        private float reportTimer;
        private bool hasRemoteTarget;
        private Vector2 remoteTarget;

        /// <summary>是否处于联机房间中（联机下放置者端上报位置、其它端跟随远端位置）</summary>
        private static bool BNetRoom =>
            NetworkManager.Instance != null
            && NetworkManager.Instance.IsConnected
            && !string.IsNullOrEmpty(NetworkManager.Instance.RoomId);

        /// <summary>
        /// 当前是否可响应麦克风（仅 PlayingPhase 开麦并上报位置；PlayingPhase 直接转入结算阶段时
        /// 阶段钩子不派发，需实时判定）。无阶段系统的调试场景视为可响应，保证 debugIgnoreOwner 可玩
        /// </summary>
        private static bool BInPlayingPhase =>
            GameFlow.GamePhaseManager.Instance == null
            || GameFlow.GamePhaseManager.Instance.CurrentPhaseAsset is GameFlow.PlayingPhase;

        private float CellSize => GridManager.Instance != null ? GridManager.Instance.PublicCellSize : 0.5f;

        private void Awake()
        {
            basePosition = transform.position;
            baseUp = transform.up;
            body = EnsureKinematicBody();
        }

        /// <summary>挂 Kinematic Rigidbody2D（无则补挂）——MovePosition 移动物理正确、能载人</summary>
        private Rigidbody2D EnsureKinematicBody()
        {
            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody2D>();
            }
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true; // 让 kinematic 平台参与碰撞通知，玩家可站立
            rb.interpolation = RigidbodyInterpolation2D.Interpolate; // 视觉平滑，与玩家插值节奏一致
            // 只冻结旋转：位置由 FixedUpdate 的 MovePosition 全权驱动；
            // 平台沿本地 up 平移，旋转 90° 后移动方向是世界 X，冻结任何位置轴都会挡住移动
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            return rb;
        }

        // ==================== 放置者标记 ====================

        /// <summary>
        /// 本端玩家是否为本道具的放置者：
        /// 联机下 OwnerKey 为 playerId，与 NetworkManager.LocalPlayerId 对比；
        /// 单机下 OwnerKey 为放置时的 PlayerController.IdentityKey，与场景本地玩家对比。
        /// 未注入放置者（测试场景直接摆放/关卡初始物体）时视为本端放置，保证可玩。
        /// </summary>
        private bool IsPlacedByLocalPlayer()
        {
            if (debugIgnoreOwner || Placed == null || string.IsNullOrEmpty(Placed.OwnerKey))
            {
                return true;
            }

            NetworkManager net = NetworkManager.Instance;
            if (net != null && !string.IsNullOrEmpty(net.LocalPlayerId))
            {
                return Placed.OwnerKey == net.LocalPlayerId;
            }

            PlayerController localPlayer = LevelPlayerRegistry.Instance != null
                ? LevelPlayerRegistry.Instance.FindLocalPlayerObject()
                : null;
            return localPlayer != null && Placed.OwnerKey == localPlayer.IdentityKey;
        }

        // ==================== 生命周期 ====================

        public override void OnPlaced()
        {
            base.OnPlaced();
            // 放置完成后的位置与朝向才是平移基准（Awake 时记录的可能是 prefab 预览姿态）
            basePosition = transform.position;
            baseUp = transform.up;
            // 联机远端生成/快照恢复可能发生在 PlayingPhase 进行中，补一次启动判定
            if (GameFlow.GamePhaseManager.Instance != null
                && GameFlow.GamePhaseManager.Instance.CurrentPhaseAsset is GameFlow.PlayingPhase)
            {
                OnRunPhaseStart();
            }
        }

        /// <summary>跑动阶段开始：本端为放置者时开始响应麦克风</summary>
        public override void OnRunPhaseStart()
        {
            responding = IsPlacedByLocalPlayer();
        }

        /// <summary>建造阶段开始：停止响应并复位到放置位置（各端本地复位，无需网络同步）</summary>
        public override void OnBuildPhaseStart()
        {
            responding = false;
            hasRemoteTarget = false;
            reportTimer = 0f;
            if (body != null)
            {
                body.position = basePosition;
            }
            transform.position = basePosition;
        }

        // ==================== 联机位置同步 ====================

        /// <summary>
        /// 远端位置广播到达（由 NetEventSync 按 player_id + item_id 路由，仅非放置者端收到）：
        /// 记录远端目标位置，FixedUpdate 中平滑跟随
        /// </summary>
        public void ApplyRemotePosition(Vector2 position)
        {
            if (responding)
            {
                return; // 放置者端由本地麦克风驱动，不应用远端位置
            }
            remoteTarget = position;
            hasRemoteTarget = true;
        }

        /// <summary>放置者端：节流上报当前位置（ItemPositionSync，服务器透传给其它端），离线为空操作</summary>
        private void ReportPositionThrottled()
        {
            if (!BNetRoom)
            {
                return;
            }
            reportTimer += Time.fixedDeltaTime;
            if (reportTimer < 1f / positionReportRate)
            {
                return;
            }
            reportTimer = 0f;
            NetEventSync.ReportItemPosition(
                ItemLifecycleSync.ResolvePrefabName(this), body.position, Facing, Mirrored);
        }

        // ==================== 声控平移 ====================

        // FixedUpdate 步进：Rigidbody2D.MovePosition 必须在物理帧调用，才能被物理系统识别为"平台移动"并托举玩家
        // 放置者端（responding）由本地麦克风驱动并节流上报位置；其它端平滑跟随远端位置；
        // 非 PlayingPhase（摆放阶段等）完全不驱动移动——摆放阶段的幽灵拖拽走 transform，
        // 若此时把刚体拉回基准位，道具会自己漂移
        private void FixedUpdate()
        {
            if (body == null)
            {
                return;
            }

            // 仅在 PlayingPhase 驱动与上报：离开 PlayingPhase（含直接进结算、钩子未派发的路径）
            // 停止麦克风驱动与位置上报，平台保持当前位置直到建造阶段本地复位
            if (responding && BInPlayingPhase)
            {
                MicVolumeManager mic = MicVolumeManager.Instance;
                float volume = mic != null && mic.IsRunning ? Mathf.Clamp01(mic.Volume) : 0f;

                // 阈值判定：超过阈值沿本地 up 升起，未达阈值沿本地 down 落下（麦克风未采集时音量为 0，自然下落）
                // 本地坐标系：旋转 90° 时"上"是世界左/右，180° 时"上"是世界下
                float targetOffset = (volume > riseThreshold ? maxUpCells : -maxDownCells) * CellSize;

                Vector2 target = basePosition + baseUp * targetOffset;
                float maxStep = moveSpeedCells * CellSize * Time.fixedDeltaTime;
                body.MovePosition(Vector2.MoveTowards(body.position, target, maxStep));
                ReportPositionThrottled();
                return;
            }

            // 非放置者端：跟随放置者端广播的位置（指数平滑收敛；偏差过大直接瞬移）
            if (hasRemoteTarget)
            {
                Vector2 current = body.position;
                if (Vector2.Distance(current, remoteTarget) > remoteTeleportCells * CellSize)
                {
                    body.MovePosition(remoteTarget);
                }
                else
                {
                    float t = 1f - Mathf.Exp(-remoteFollowSpeed * Time.fixedDeltaTime);
                    body.MovePosition(Vector2.Lerp(current, remoteTarget, t));
                }
            }
        }
    }
}
