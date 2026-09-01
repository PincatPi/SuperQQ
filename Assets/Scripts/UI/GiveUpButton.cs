using SuperQQ.Player;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// 局内"放弃"按钮（挂在 Level1/Level2 的 GiveupBtn 上）。
    /// 点击后先弹出二次确认弹窗（confirmDialogPrefab，弹窗内按钮字段指向 prefab 内部按钮，
    /// 实例化时按相对路径解析到实例上的对应按钮）：
    ///   - 确认放弃：让本地主控玩家强制死亡（等价于 PlayingPhase 长按 B 三秒的放弃逻辑）——
    ///     视为不可豁免死亡，播放死亡动画并在死亡位置进入幽灵状态；
    ///   - 取消：关闭弹窗，回到对局。
    /// 未配置 confirmDialogPrefab 时点击直接放弃（兼容无弹窗用法）。
    ///
    /// 使用方式：挂在按钮上自动绑定自身 Button.onClick；也可在 prefab 的
    /// Button.onClick 中直接绑定 OnGiveUpClicked（button 字段留空即可）。
    /// </summary>
    public class GiveUpButton : MonoBehaviour
    {
        [Header("可选：留空则自动取自身 Button 并绑定点击事件")]
        [SerializeField] private Button button;

        [Header("二次确认弹窗 prefab（留空则点击直接放弃）")]
        [SerializeField] private GameObject confirmDialogPrefab;
        [Header("弹窗内按钮（引用 prefab 内部按钮即可，实例化时自动解析）")]
        [SerializeField] private Button confirmGiveUpButton;
        [SerializeField] private Button cancelButton;

        [Header("行为参数")]
        [Tooltip("确认放弃时是否播放命中音效；放弃属于非命中死亡，默认不播放")]
        [SerializeField] private bool playHitSfx = false;

        [Tooltip("确认放弃时是否按越界死亡处理（true 时幽灵重生在地图中央，false 时保持死亡位置）")]
        [SerializeField] private bool fellOutOfBounds = false;

        // 确认弹窗实例（懒创建，之后复用）
        private GameObject _dialogInstance;

        // 弹窗是否打开中
        private bool _bDialogOpen;

        private void Awake()
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }
            if (button != null)
            {
                button.onClick.AddListener(OnGiveUpClicked);
            }
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(OnGiveUpClicked);
            }
            // 弹窗挂在非本物体下（本场景 Canvas 或自建 Canvas），本物体销毁时一并清掉
            if (_dialogInstance != null)
            {
                Destroy(_dialogInstance);
                _dialogInstance = null;
            }
        }

        /// <summary>按钮点击入口：也可在 prefab 的 Button.onClick 里直接绑定本方法</summary>
        public void OnGiveUpClicked()
        {
            if (_bDialogOpen)
            {
                return;
            }

            // 未配置确认弹窗：点击即放弃
            if (confirmDialogPrefab == null)
            {
                ExecuteGiveUp();
                return;
            }

            ShowConfirmDialog();
        }

        // ==================== 二次确认弹窗 ====================

        private void ShowConfirmDialog()
        {
            EnsureDialogInstance();
            if (_dialogInstance == null)
            {
                // 弹窗搭建失败：退化为直接放弃，避免按钮失效
                Debug.LogWarning("[UI] 放弃二次确认弹窗搭建失败，直接执行放弃");
                ExecuteGiveUp();
                return;
            }

            _bDialogOpen = true;
            _dialogInstance.SetActive(true);
            _dialogInstance.transform.SetAsLastSibling(); // 置顶显示
        }

        private void HideConfirmDialog()
        {
            _bDialogOpen = false;
            if (_dialogInstance != null)
            {
                _dialogInstance.SetActive(false);
            }
        }

        /// <summary>懒创建弹窗实例并绑定两个按钮（字段引用的是 prefab 内部按钮，需解析到实例）</summary>
        private void EnsureDialogInstance()
        {
            if (_dialogInstance != null)
            {
                return;
            }

            // 弹窗 prefab 自带 Canvas 时作为根实例化（进当前场景）；否则挂到本场景的 Canvas 下
            //——不挂跨场景常驻 Canvas，避免离开本关后弹窗残留
            Transform parent = null;
            if (confirmDialogPrefab.GetComponentInChildren<Canvas>(true) == null)
            {
                Canvas canvas = FindSceneCanvas();
                if (canvas != null)
                {
                    parent = canvas.transform;
                }
            }
            _dialogInstance = Instantiate(confirmDialogPrefab, parent, false);
            _dialogInstance.name = confirmDialogPrefab.name;
            _dialogInstance.SetActive(false);

            Button confirm = ResolveInstanceButton(confirmGiveUpButton);
            if (confirm != null)
            {
                confirm.onClick.AddListener(OnConfirmGiveUpClicked);
            }
            else
            {
                Debug.LogWarning("[UI] 放弃二次确认弹窗未配置确认按钮，将无法放弃", _dialogInstance);
            }

            Button cancel = ResolveInstanceButton(cancelButton);
            if (cancel != null)
            {
                cancel.onClick.AddListener(OnCancelClicked);
            }
            else
            {
                Debug.LogWarning("[UI] 放弃二次确认弹窗未配置取消按钮", _dialogInstance);
            }
        }

        /// <summary>按 prefab 内的相对路径，把序列化的 prefab 按钮引用解析为实例上的对应按钮</summary>
        private Button ResolveInstanceButton(Button prefabButton)
        {
            if (prefabButton == null || _dialogInstance == null)
            {
                return null;
            }

            string path = GetRelativePath(prefabButton.transform, confirmDialogPrefab.transform);
            if (path == null)
            {
                Debug.LogWarning($"[UI] 按钮 {prefabButton.name} 不在确认弹窗 prefab 内，无法解析", prefabButton);
                return null;
            }

            Transform target = path.Length == 0
                ? _dialogInstance.transform
                : _dialogInstance.transform.Find(path);
            return target != null ? target.GetComponent<Button>() : null;
        }

        /// <summary>child 相对 root 的层级路径；child 不在 root 下时返回 null</summary>
        private static string GetRelativePath(Transform child, Transform root)
        {
            if (child == root)
            {
                return "";
            }

            string path = child.name;
            Transform current = child.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return current == root ? path : null;
        }

        /// <summary>找本场景内的 Canvas（跳过跨场景常驻 Canvas）</summary>
        private Canvas FindSceneCanvas()
        {
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c != null && c.gameObject.scene == gameObject.scene)
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>弹窗"确认放弃"</summary>
        private void OnConfirmGiveUpClicked()
        {
            HideConfirmDialog();
            ExecuteGiveUp();
        }

        /// <summary>弹窗"取消"</summary>
        private void OnCancelClicked()
        {
            HideConfirmDialog();
        }

        // ==================== 放弃流程 ====================

        /// <summary>
        /// 让本端主控的本地玩家进入死亡状态（主动放弃），不影响其他本地/远程玩家。
        /// 与 PlayingPhase.GiveUpLocalPlayer 逻辑一致：死亡/幽灵/通关/冻结中的玩家跳过，
        /// 强制死亡无视无敌状态，幽灵在死亡位置重生。
        /// </summary>
        private void ExecuteGiveUp()
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

            player.PlayerForceDie(playHitSfx, fellOutOfBounds);
        }
    }
}
