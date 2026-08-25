using SuperQQ.Grid;
using UnityEngine;

namespace SuperQQ.Map
{
    /// <summary>
    /// 船平台 — 把船身 footprint 格子登记进网格占据表，并跟随船移动（涨潮上升）。
    /// 登记后：普通道具无法摆进船身格；黄油块可凭实心碰撞体承载物豁免叠放在船面；
    /// 船随水位移动时按新锚点自动释放旧格、登记新格，占据标记始终与视觉对齐。
    /// </summary>
    [RequireComponent(typeof(FootprintBoxView))]
    public class BoatPlatform : MonoBehaviour
    {
        [Header("调试")]
        [Tooltip("在游戏中显示 footprint 虚线框（默认隐藏；编辑期 Scene Gizmo 不受此开关影响）")]
        [SerializeField] private bool showDebugBox;

        private FootprintBoxView box;
        private PlacedItem placed;
        private Vector2Int registeredAnchor;
        private bool registered;

        // 网格吸附是否已完成（GridManager 就绪前每帧重试，避免 Start 时序导致吸附被跳过）
        private bool snapDone;

        private void Start()
        {
            box = GetComponent<FootprintBoxView>();

            // 生成虚线框（默认游戏内隐藏，仅调试参照；根节点缩放必须为 1）
            if (box != null)
            {
                box.Init(box.Footprint, 0);
                if (!showDebugBox)
                {
                    box.Hide();
                }
            }
            TrySnap();
            TryRegister();
        }

        private void Update()
        {
            // 网格吸附：白天静止期持续自校准（GridManager 就绪时序、昼夜还原冲掉吸附等情况
            // 都能自愈）；黑夜/过渡期不吸附，避免与涨潮升降冲突
            TrySnap();

            // 船位置变化（涨潮）时按新锚点重登记（格子量化，仅在锚点变化时操作）
            Vector2Int anchor = ComputeAnchor();
            if (!registered || anchor != registeredAnchor)
            {
                Unregister();
                TryRegister(anchor);
            }
        }

        /// <summary>
        /// 网格吸附：把根节点（框中心）对齐到 footprint 对应的网格中心。
        /// 仅在"白天且非过渡中"执行——涨潮/退潮过渡期间吸附会与升降动画冲突
        /// </summary>
        private bool TrySnap()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || box == null)
            {
                return false;
            }
            MapDayNightController dayNight = MapDayNightController.Instance;
            if (dayNight != null && (dayNight.IsNight || dayNight.Blend > 0.01f))
            {
                return false; // 黑夜或昼夜过渡中：位置由 MapDayNightController 驱动，不吸附
            }
            Vector2Int anchor = ComputeAnchor();
            Vector2 snapped = grid.GetPlacementWorldPos(anchor, box.Footprint, 0);
            if (((Vector2)transform.position - snapped).sqrMagnitude < 0.0001f)
            {
                snapDone = true;
                return true; // 已对齐，无需写回（避免每帧无意义赋值）
            }
            transform.position = snapped;
            snapDone = true;
            return true;
        }

        private void OnDestroy()
        {
            Unregister();
        }

        /// <summary>当前锚点格子（footprint 左下角；根节点枢轴在框中心）</summary>
        private Vector2Int ComputeAnchor()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || box == null)
            {
                return registeredAnchor;
            }
            float cs = grid.PublicCellSize;
            Vector2 bottomLeft = (Vector2)transform.position
                - new Vector2(box.Footprint.x * 0.5f * cs, box.Footprint.y * 0.5f * cs);
            return grid.WorldToCell(bottomLeft + new Vector2(cs * 0.5f, cs * 0.5f));
        }

        private void TryRegister()
        {
            TryRegister(ComputeAnchor());
        }

        private void TryRegister(Vector2Int anchor)
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || box == null || registered)
            {
                return;
            }
            placed = gameObject.AddComponent<PlacedItem>();
            placed.Init(null, anchor, 0, -1); // 关卡物体：无 Def、无属主
            grid.Occupy(anchor, box.Footprint, placed, 0);
            registeredAnchor = anchor;
            registered = true;
        }

        private void Unregister()
        {
            if (!registered)
            {
                return;
            }
            GridManager grid = GridManager.Instance;
            if (grid != null)
            {
                grid.Release(placed);
            }
            if (placed != null)
            {
                Destroy(placed);
                placed = null;
            }
            registered = false;
        }
    }
}
