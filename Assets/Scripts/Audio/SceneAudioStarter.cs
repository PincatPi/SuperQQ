using UnityEngine;

namespace SuperQQ.Audio
{
    /// <summary>
    /// 场景音频启动器：场景加载完成后（Start）自动播放本场景的 BGM。
    ///
    /// 用法：在场景中任意常驻物体上挂载本组件，配置要播放的 SfxId 即可，无需写代码。
    ///   - 大厅类场景（Home/Lobby/Room/CharacterSelect）：Bgm = BgmLobby
    ///   - 关卡场景（Level1 等）：Bgm = BgmLevel
    ///
    /// 切换行为：
    ///   - AudioManager 跨场景常驻，场景切换时 BGM 自动交叉淡化过渡；
    ///   - 相同 BGM 的场景间切换（如大厅各界面互跳）不会重启音乐（内部幂等）；
    ///   - 某场景不切换 BGM 时保持 None，沿用上一场景音乐。
    /// </summary>
    public class SceneAudioStarter : MonoBehaviour
    {
        [Header("背景音乐")]
        [Tooltip("本场景要播放的 BGM（Clip 在 AudioCatalog 中配置）；None 表示本场景不切换 BGM")]
        [SerializeField] private SfxId _bgm = SfxId.None;

        [Header("过渡")]
        [Tooltip("交叉淡化时长（秒）")]
        [SerializeField, Min(0f)] private float _fadeTime = 1f;

        private void Start()
        {
            if (_bgm != SfxId.None)
            {
                AudioManager.PlayMusic(_bgm, _fadeTime);
            }
        }
    }
}
