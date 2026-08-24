using UnityEngine;

/// <summary>
/// 海浪帧动画驱动器：按固定频率循环切换 SpriteRenderer 的 sprite。
/// 所有海浪小块共用同一组帧，通过 startFrameIndex 错开相位，拼接出完整海浪起伏效果。
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class WaveFrameAnimator : MonoBehaviour
{
    [Header("帧配置（所有海浪共用同一组，按播放顺序排列）")]
    [SerializeField] private Sprite[] frames;

    [Header("切换频率（帧/秒），所有海浪保持一致")]
    [SerializeField] private float framesPerSecond = 8f;

    [Header("起始帧序号（frames 数组下标，各海浪错开以形成相位差）")]
    [SerializeField] private int startFrameIndex;

    private SpriteRenderer spriteRenderer;
    private int currentFrameIndex;
    private float timer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        if (frames == null || frames.Length == 0)
        {
            enabled = false;
            return;
        }

        currentFrameIndex = ((startFrameIndex % frames.Length) + frames.Length) % frames.Length;
        spriteRenderer.sprite = frames[currentFrameIndex];
    }

    private void Update()
    {
        if (framesPerSecond <= 0f) return;

        timer += Time.deltaTime;
        float frameInterval = 1f / framesPerSecond;

        while (timer >= frameInterval)
        {
            timer -= frameInterval;
            currentFrameIndex = (currentFrameIndex + 1) % frames.Length;
            spriteRenderer.sprite = frames[currentFrameIndex];
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (frames != null && frames.Length > 0)
        {
            startFrameIndex = Mathf.Clamp(startFrameIndex, 0, frames.Length - 1);
        }
        framesPerSecond = Mathf.Max(0f, framesPerSecond);
    }
#endif
}
