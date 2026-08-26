using UnityEngine;

namespace SuperQQ.Item
{
    /// <summary>
    /// 序列帧特效播放器 — 生成后按 fps 顺序播放帧序列一次，播完自动销毁。
    /// 爆炸等一次性特效用（挂在带 SpriteRenderer 的物体上，生成即播，无需 Animator）
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteSequenceEffect : MonoBehaviour
    {
        [Tooltip("帧序列（按播放顺序）")]
        [SerializeField] private Sprite[] frames;
        [Tooltip("播放帧率（帧/秒）")]
        [SerializeField, Min(1f)] private float fps = 14f;
        [Tooltip("播放完毕后销毁自身")]
        [SerializeField] private bool destroyOnFinish = true;

        private SpriteRenderer sr;
        private int index;
        private float timer;

        private void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            if (frames != null && frames.Length > 0)
            {
                sr.sprite = frames[0];
            }
        }

        private void Update()
        {
            if (frames == null || frames.Length == 0)
            {
                return;
            }

            timer += Time.deltaTime;
            int target = Mathf.Min((int)(timer * fps), frames.Length - 1);
            if (target != index)
            {
                index = target;
                sr.sprite = frames[index];
            }
            if (timer * fps >= frames.Length && destroyOnFinish)
            {
                Destroy(gameObject);
            }
        }
    }
}
