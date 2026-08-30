using System.Collections.Generic;
using SuperQQ.Network;
using SuperQQ.Score;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SuperQQ.Settlement
{
    /// <summary>
    /// 最终结算控制器。
    /// 进入最终结算场景后，从 PlayerScoreManager 读取累计排名：
    ///   - MVP 区展示冠军（名称 + 图标染玩家色）
    ///   - 排名区按行展示名次/玩家/总分
    /// 返回房间按钮：联机下回房间场景（房间状态由服务器驱动，UIRoomController 自动接管）；
    /// 单机/离线回大厅 Home。
    /// </summary>
    public class FinalSettlementController : MonoBehaviour
    {
        [Header("MVP 区")]
        [SerializeField] private TMP_Text mvpName;
        [SerializeField] private Image mvpPlayerIcon;
        [Tooltip("胜利玩家 PlayerIcon（取自跨场景档案缓存的 SelectionIconSprite，原色显示；无图标时隐藏）")]
        [SerializeField] private Image winnerPlayerIcon;

        [Header("排名区（运行时按玩家人数生成行）")]
        [Tooltip("排名行 prefab（内含 RankText/PlayerName/Avatar/Initial，按名解析）")]
        [SerializeField] private GameObject rankingRowPrefab;
        [Tooltip("排名行容器（RankingListContainer）")]
        [SerializeField] private Transform rankingRowsContainer;

        [Header("按钮")]
        [SerializeField] private Button backRoomButton;
        [SerializeField] private Button backLobbyButton;
        [Tooltip("返回房间场景名")]
        [SerializeField] private string roomSceneName = "Room";
        [Tooltip("离线/单机兜底场景（大厅）")]
        [SerializeField] private string lobbySceneName = "Home";

        private void Start()
        {
            if (backRoomButton != null)
            {
                backRoomButton.onClick.AddListener(OnBackRoomClicked);
            }
            if (backLobbyButton != null)
            {
                backLobbyButton.onClick.AddListener(OnBackLobbyClicked);
            }
            RefreshFinalSettlement();
        }

        private void OnDestroy()
        {
            if (backRoomButton != null)
            {
                backRoomButton.onClick.RemoveListener(OnBackRoomClicked);
            }
            if (backLobbyButton != null)
            {
                backLobbyButton.onClick.RemoveListener(OnBackLobbyClicked);
            }
        }

        /// <summary>刷新最终结算展示（冠军 + 排名行）</summary>
        public void RefreshFinalSettlement()
        {
            if (PlayerScoreManager.Instance == null)
            {
                Debug.LogError("[FinalSettlementController] PlayerScoreManager 不存在，无法刷新最终结算。");
                return;
            }

            List<string> rankedPlayerNames = PlayerScoreManager.Instance.GetRankedPlayerNames();
            if (rankedPlayerNames.Count == 0)
            {
                if (mvpName != null)
                {
                    mvpName.text = "暂无玩家数据";
                }
                BuildRankingRows(rankedPlayerNames); // 空列表：清空行
                return;
            }

            // MVP：第一名
            string winnerName = rankedPlayerNames[0];
            int winnerScore = PlayerScoreManager.Instance.GetPlayerTotalScore(winnerName);
            if (mvpName != null)
            {
                mvpName.text = $"{winnerName}　{winnerScore}分";
            }
            if (mvpPlayerIcon != null)
            {
                mvpPlayerIcon.color = Color.white; // 记录中无颜色字段，保持素材原色
            }

            // 胜利玩家 PlayerIcon：取跨场景档案缓存的 SelectionIconSprite（关卡内由化身回写），原色显示
            ApplyWinnerIcon(winnerName);

            // 排名行：运行时按玩家人数生成（场景中不预置行）
            BuildRankingRows(rankedPlayerNames);
        }

        /// <summary>应用胜利玩家 PlayerIcon：无档案/无图标时隐藏 Image，避免显示纯色块</summary>
        private void ApplyWinnerIcon(string winnerName)
        {
            if (winnerPlayerIcon == null)
            {
                return;
            }

            Sprite icon = null;
            if (SuperQQ.Player.PlayerSessionManager.Instance != null)
            {
                SuperQQ.Player.PlayerProfile profile =
                    SuperQQ.Player.PlayerSessionManager.Instance.GetProfile(winnerName);
                icon = profile != null ? profile.SelectionIcon : null;
            }

            winnerPlayerIcon.sprite = icon;
            winnerPlayerIcon.color = Color.white;
            winnerPlayerIcon.preserveAspect = true;
            winnerPlayerIcon.enabled = icon != null;
        }

        /// <summary>清空并按排名数据重新生成排名行</summary>
        private void BuildRankingRows(List<string> rankedPlayerNames)
        {
            if (rankingRowPrefab == null || rankingRowsContainer == null)
            {
                Debug.LogWarning("[FinalSettlementController] 排名行 prefab/容器未配置，跳过排名生成。", this);
                return;
            }

            for (int i = rankingRowsContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(rankingRowsContainer.GetChild(i).gameObject);
            }

            for (int i = 0; i < rankedPlayerNames.Count; i++)
            {
                GameObject rowGo = Instantiate(rankingRowPrefab, rankingRowsContainer);
                string name = rankedPlayerNames[i];
                int score = PlayerScoreManager.Instance.GetPlayerTotalScore(name);

                TMP_Text rankText = FindText(rowGo.transform, "RankText");
                TMP_Text nameText = FindText(rowGo.transform, "PlayerName");
                TMP_Text initial = FindText(rowGo.transform, "Avatar/Initial");
                if (rankText != null)
                {
                    rankText.text = (i + 1).ToString();
                }
                if (nameText != null)
                {
                    nameText.text = $"{name}　{score}分";
                }
                if (initial != null)
                {
                    initial.text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
                }
            }
        }

        private static TMP_Text FindText(Transform root, string path)
        {
            Transform t = root.Find(path);
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }

        /// <summary>返回房间：联机下回 Room 场景（房间未解散则继续等待房主开局）；离线回大厅</summary>
        private void OnBackRoomClicked()
        {
            NetworkManager net = NetworkManager.Instance;
            if (net != null && net.IsConnected && !string.IsNullOrEmpty(net.RoomId))
            {
                Debug.Log("[FinalSettlement] 返回房间");
                SceneManager.LoadScene(roomSceneName);
            }
            else
            {
                SceneManager.LoadScene(lobbySceneName);
            }
        }

        /// <summary>再来一局：同返回房间（是否重开一局由房间内流程决定，后端不支持重开时回大厅）</summary>
        private void OnBackLobbyClicked()
        {
            OnBackRoomClicked();
        }
    }
}
