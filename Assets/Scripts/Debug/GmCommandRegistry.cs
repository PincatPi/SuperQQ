using System;
using System.Collections.Generic;
using System.Text;

namespace SuperQQ.Debugging
{
    /// <summary>
    /// GM 指令注册表（纯 C#，不依赖 UnityEngine / 网络层，可独立单元测试）。
    /// 客户端侧指令（kill_me、freeze_me 等）在此注册处理器；
    /// 服务器透传的 GmCommandPush 与离线模式下的本地执行都经由本表分发。
    /// 新增指令只需调用一次 Register，无需改动其他模块。
    /// </summary>
    public static class GmCommandRegistry
    {
        /// <summary>指令处理器：入参为指令参数，返回控制台回显文本（可返回 null 表示无回显）</summary>
        public delegate string GmCommandHandler(string[] args);

        private class Entry
        {
            public string Description;
            public GmCommandHandler Handler;
        }

        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>注册指令（指令名不区分大小写，重复注册覆盖）</summary>
        public static void Register(string name, string description, GmCommandHandler handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null) return;
            entries[name] = new Entry { Description = description ?? "", Handler = handler };
        }

        /// <summary>尝试执行指令；未注册返回 false，已注册返回 true 且 feedback 为处理器回显</summary>
        public static bool TryExecute(string name, string[] args, out string feedback)
        {
            feedback = null;
            if (string.IsNullOrEmpty(name)) return false;
            if (!entries.TryGetValue(name, out Entry entry)) return false;

            try
            {
                feedback = entry.Handler(args ?? Array.Empty<string>());
            }
            catch (Exception e)
            {
                feedback = $"指令执行异常: {e.Message}";
            }
            return true;
        }

        /// <summary>生成 help 指令的展示文本（仅含客户端本地指令，服务器指令由服务器侧 help 回复）</summary>
        public static string BuildHelpText()
        {
            var sb = new StringBuilder("本地可用指令:");
            foreach (KeyValuePair<string, Entry> pair in entries)
            {
                sb.Append('\n').Append("  ").Append(pair.Key);
                if (!string.IsNullOrEmpty(pair.Value.Description))
                {
                    sb.Append(" - ").Append(pair.Value.Description);
                }
            }
            return sb.ToString();
        }
    }
}
