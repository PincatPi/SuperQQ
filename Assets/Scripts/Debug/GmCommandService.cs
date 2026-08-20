using System;
using System.Linq;
using Minigame.Room.V1;
using SuperQQ.Network;
using SuperQQ.Player;
using UnityEngine;

namespace SuperQQ.Debugging
{
    /// <summary>
    /// GM 指令网络服务（场景无关单例，随 GmDebugConsole 常驻）。
    /// 职责：
    ///   1. 将控制台输入打包为 GmCommandRequest 发给服务器（在线且在房间时）；
    ///   2. 接收 GmCommandResponse / GmCommandPush，透传指令经 GmCommandRegistry 本地执行；
    ///   3. 离线（未连接/未进房）时直接本地执行，便于单人调试。
    /// 通过 Output 事件向控制台 UI 广播回显文本。
    /// 仅在编辑器与 Development Build 中创建，Release 包不包含。
    /// </summary>
    public class GmCommandService : MonoBehaviour
    {
        public static GmCommandService Instance { get; private set; }

        /// <summary>控制台回显事件（UI 订阅）</summary>
        public event Action<string> Output;

        private NetworkManager _boundNet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Instance != null) return;
            var go = new GameObject("GmDebugConsole");
            DontDestroyOnLoad(go);
            go.AddComponent<GmCommandService>();
            go.AddComponent<GmConsoleUI>();
#endif
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            RegisterBuiltinCommands();
        }

        private void OnDestroy()
        {
            Unbind();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // NetworkManager 由大厅流程后创建，这里惰性绑定并在其实例更换时重绑
            NetworkManager net = NetworkManager.Instance;
            if (net == _boundNet) return;
            Unbind();
            if (net != null) Bind(net);
        }

        private void Bind(NetworkManager net)
        {
            _boundNet = net;
            net.Register<GmCommandResponse>(OnGmCommandResponse);
            net.Register<GmCommandPush>(OnGmCommandPush);
        }

        private void Unbind()
        {
            if (_boundNet == null) return;
            _boundNet.Unregister<GmCommandResponse>();
            _boundNet.Unregister<GmCommandPush>();
            _boundNet = null;
        }

        /// <summary>提交一行指令（可带前导 "/"，参数以空白分隔）</summary>
        public void Submit(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return;

            string line = rawLine.Trim().Trim('`');
            if (line.StartsWith("/")) line = line.Substring(1);

            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string name = parts[0];
            string[] args = parts.Skip(1).ToArray();

            NetworkManager net = NetworkManager.Instance;
            bool online = net != null && net.IsConnected && !string.IsNullOrEmpty(net.RoomId);
            if (!online)
            {
                if (GmCommandRegistry.TryExecute(name, args, out string localFeedback))
                {
                    Emit(string.IsNullOrEmpty(localFeedback) ? $"已执行(本地): {name}" : localFeedback);
                }
                else
                {
                    Emit($"未连接房间，本地无指令 '{name}'（help 查看本地指令）");
                }
                return;
            }

            net.Send(new GmCommandRequest
            {
                RoomId = net.RoomId,
                PlayerId = net.LocalPlayerId,
                Command = name,
                Args = { args }
            });
            Emit($"已发送: {line}");
        }

        private void OnGmCommandResponse(GmCommandResponse resp)
        {
            Emit(string.IsNullOrEmpty(resp.Message) ? "服务器已受理（无回执内容）" : resp.Message);
        }

        private void OnGmCommandPush(GmCommandPush push)
        {
            // 定向指令只在本端是指定目标时生效
            NetworkManager net = NetworkManager.Instance;
            if (!string.IsNullOrEmpty(push.TargetPlayerId)
                && net != null
                && push.TargetPlayerId != net.LocalPlayerId) return;

            if (GmCommandRegistry.TryExecute(push.Command, push.Args.ToArray(), out string feedback))
            {
                Emit(string.IsNullOrEmpty(feedback) ? $"服务器指令已执行: {push.Command}" : feedback);
            }
            else
            {
                Emit($"收到未知客户端指令: {push.Command}");
            }
        }

        private void Emit(string message)
        {
            Debug.Log($"[GM] {message}");
            Output?.Invoke(message);
        }

        // ==================== 内置客户端指令 ====================

        private void RegisterBuiltinCommands()
        {
            GmCommandRegistry.Register("help", "列出全部本地可用指令",
                _ => GmCommandRegistry.BuildHelpText());

            GmCommandRegistry.Register("kill_me", "本地玩家立即死亡（走正常死亡链路，自动上报出局）", _ =>
            {
                PlayerController player = FindLocalPlayer();
                if (player == null) return "未找到本地玩家";
                player.PlayerDie();
                return "本地玩家已死亡";
            });

            GmCommandRegistry.Register("freeze_me", "冻结本地玩家", _ =>
            {
                PlayerController player = FindLocalPlayer();
                if (player == null) return "未找到本地玩家";
                player.Freeze();
                return "本地玩家已冻结";
            });

            GmCommandRegistry.Register("unfreeze_me", "解冻本地玩家", _ =>
            {
                PlayerController player = FindLocalPlayer();
                if (player == null) return "未找到本地玩家";
                player.Unfreeze();
                return "本地玩家已解冻";
            });
        }

        private static PlayerController FindLocalPlayer()
        {
            LevelPlayerRegistry registry = LevelPlayerRegistry.Instance;
            if (registry == null) return null;
            System.Collections.Generic.IReadOnlyList<PlayerController> players = registry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].BIsLocal) return players[i];
            }
            return null;
        }
    }
}
