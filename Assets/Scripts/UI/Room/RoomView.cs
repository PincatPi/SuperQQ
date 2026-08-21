using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 房间等待界面视图（UI/Room 场景）— 纯展示层。
    /// 只负责把数据渲染到场景 UI，不含任何网络/业务逻辑；
    /// 由控制器（UIRoomController）单向驱动，按钮点击通过 ReadyClicked 事件上抛。
    ///
    /// Editor 接线：
    ///   roomCodeText     ← Canvas/TopArea/TopRight/RoomCodeText
    ///   progressText     ← Canvas/BottomArea/ProgressPanel/ProgressText
    ///   barFill          ← ProgressPanel 下的 BarFill（Image Type 需设为 Filled / Horizontal）
    ///   readyButton      ← Canvas/BottomArea/BtnReady
    ///   readyButtonLabel ← BtnReady 下的 Label（TMP）
    /// </summary>
    public class RoomView : MonoBehaviour
    {
        [Header("玩家槽位（按顺序拖入 Slot_1~4，运行时按房间人数显隐）")]
        [SerializeField] private GameObject[] slots;

        [Header("房间码")]
        [SerializeField] private TMP_Text roomCodeText;
        [SerializeField] private string roomCodePrefix = "房间码 ";

        [Header("准备进度")]
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private Image barFill;
        [SerializeField] private string progressFormat = "{0} / {1} 已准备";

        [Header("准备/开始按钮（同一按钮，按身份切换模式）")]
        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyButtonLabel;
        [SerializeField] private string notReadyText = "点击准备";
        [SerializeField] private string readyText = "取消准备";
        [SerializeField] private string startGameText = "开始游戏";

        /// <summary>准备模式下按钮被点击（由控制器订阅，处理网络逻辑）</summary>
        public event Action ReadyClicked;
        /// <summary>开始游戏模式下按钮被点击（由控制器订阅，处理网络逻辑）</summary>
        public event Action StartClicked;

        // 当前按钮模式：false=准备切换（普通玩家），true=开始游戏（房主）
        private bool _isStartMode;

        // 槽位子视图缓存（运行时从 slots 上获取/自动挂载 RoomSlotView）
        private RoomSlotView[] _slotViews;

        private void Awake()
        {
            if (readyButton != null)
            {
                readyButton.onClick.AddListener(OnReadyButtonClicked);
            }

            // 运行时兜底：BarFill 必须是 Filled/Horizontal，fillAmount 才生效。
            // 场景里若被配成 Simple（或被编辑器保存覆盖），这里强制纠正。
            if (barFill != null && barFill.type != Image.Type.Filled)
            {
                barFill.type = Image.Type.Filled;
                barFill.fillMethod = Image.FillMethod.Horizontal;
                barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            }

            // 槽位默认全部隐藏，等待房间数据驱动
            SetPlayerCount(0);
        }

        private void OnDestroy()
        {
            if (readyButton != null)
            {
                readyButton.onClick.RemoveListener(OnReadyButtonClicked);
            }
        }

        /// <summary>显示房间码</summary>
        public void SetRoomCode(string roomCode)
        {
            if (roomCodeText != null) roomCodeText.text = roomCodePrefix + roomCode;
        }

        /// <summary>按玩家数量显隐槽位：前 count 个槽位显示（P1、P2…依入座顺序），其余隐藏</summary>
        public void SetPlayerCount(int count)
        {
            if (slots == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) slots[i].SetActive(i < count);
            }
        }

        /// <summary>填充指定槽位的玩家信息（昵称 + 准备状态文本）</summary>
        public void SetSlotPlayer(int index, string playerName, bool isReady)
        {
            RoomSlotView slot = GetSlotView(index);
            if (slot != null) slot.SetPlayer(playerName, isReady);
        }

        /// <summary>获取槽位子视图：未挂 RoomSlotView 时自动挂载（其 Awake 会按名字自动绑定子物体）</summary>
        private RoomSlotView GetSlotView(int index)
        {
            if (slots == null || index < 0 || index >= slots.Length || slots[index] == null) return null;
            if (_slotViews == null || _slotViews.Length != slots.Length)
            {
                _slotViews = new RoomSlotView[slots.Length];
            }
            if (_slotViews[index] == null)
            {
                _slotViews[index] = slots[index].GetComponent<RoomSlotView>();
                if (_slotViews[index] == null)
                {
                    _slotViews[index] = slots[index].AddComponent<RoomSlotView>();
                }
            }
            return _slotViews[index];
        }

        /// <summary>刷新准备进度：文本 "n / m 已准备" + 进度条填充比例</summary>
        public void SetReadyProgress(int readyCount, int totalCount)
        {
            if (progressText != null)
            {
                progressText.text = string.Format(progressFormat, readyCount, totalCount);
            }
            if (barFill != null)
            {
                barFill.fillAmount = totalCount > 0 ? Mathf.Clamp01((float)readyCount / totalCount) : 0f;
            }
        }

        /// <summary>切到准备模式（普通玩家）：未准备显示"点击准备"，已准备显示"取消准备"</summary>
        public void SetReadyMode(bool isReady)
        {
            _isStartMode = false;
            if (readyButtonLabel != null)
            {
                readyButtonLabel.text = isReady ? readyText : notReadyText;
            }
            if (readyButton != null) readyButton.interactable = true;
        }

        /// <summary>切到开始游戏模式（房主）：canStart=false 时按钮置灰不可点</summary>
        public void SetStartMode(bool canStart)
        {
            _isStartMode = true;
            if (readyButtonLabel != null)
            {
                readyButtonLabel.text = startGameText;
            }
            if (readyButton != null) readyButton.interactable = canStart;
        }

        /// <summary>设置按钮是否可交互（未进房时禁用）</summary>
        public void SetReadyInteractable(bool interactable)
        {
            if (readyButton != null) readyButton.interactable = interactable;
        }

        private void OnReadyButtonClicked()
        {
            if (_isStartMode)
            {
                StartClicked?.Invoke();
            }
            else
            {
                ReadyClicked?.Invoke();
            }
        }
    }
}
