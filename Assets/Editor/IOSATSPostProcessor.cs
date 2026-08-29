#if UNITY_IOS && UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

/// <summary>
/// iOS 构建后处理：向 Info.plist 写入 ATS（App Transport Security）例外，
/// 放行明文 ws:// 连接（网关为裸 IP，NSExceptionDomains 对 IP 不生效，
/// 只能使用 NSAllowsArbitraryLoads）。
/// 上架 App Store 时如需收紧，请改为 wss:// + 域名证书后可移除此脚本。
/// </summary>
public static class IOSATSPostProcessor
{
    [PostProcessBuild(999)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        var ats = plist.root.CreateDict("NSAppTransportSecurity");
        ats.SetBoolean("NSAllowsArbitraryLoads", true);

        plist.WriteToFile(plistPath);
        UnityEngine.Debug.Log("[IOSATS] 已向 Info.plist 写入 NSAllowsArbitraryLoads = true");
    }
}
#endif
