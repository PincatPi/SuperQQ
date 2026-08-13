using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class ProceduralPaperGrid : MonoBehaviour
{
    [Header("Grid Structure")]
    [SerializeField, Min(32f)] private float majorCellPixels = 128f;
    [SerializeField, Range(3, 8)] private int subdivisions = 5;

    [Header("Stroke")]
    [SerializeField, Range(0.5f, 6f)] private float majorLineWidth = 2.2f;
    [SerializeField, Range(0.25f, 3f)] private float minorLineWidth = 0.9f;
    [SerializeField, Range(0f, 1f)] private float minorOpacity = 0.34f;
    [SerializeField, Range(0f, 8f)] private float wobble = 1.8f;
    [SerializeField, Range(0.15f, 0.8f)] private float dashFill = 0.36f;

    [Header("Palette")]
    [SerializeField] private Color paperColor = new Color32(246, 241, 229, 255);
    [SerializeField, Range(0f, 1f)] private float paperOpacity = 0.6f;
    [SerializeField] private Color minorColor = new Color32(118, 151, 160, 255);
    [SerializeField] private Color majorColor = new Color32(45, 98, 120, 255);
    [SerializeField, Range(0f, 0.08f)] private float paperGrain = 0.025f;

    [Header("Output")]
    [SerializeField, Range(256, 1024)] private int textureSize = 1024;
    [SerializeField] private Camera targetCamera;
    [SerializeField, Range(1f, 1.2f)] private float viewportOverscan = 1.05f;

    private SpriteRenderer spriteRenderer;
    private Texture2D generatedTexture;
    private Sprite generatedSprite;
    private float lastOrthoSize = -1f;
    private float lastAspect = -1f;

    private void OnEnable()
    {
        EnsureRenderer();
        Regenerate();
        FitToCamera(true);
    }

    private void OnValidate()
    {
        majorCellPixels = Mathf.Max(32f, majorCellPixels);
        subdivisions = Mathf.Clamp(subdivisions, 3, 8);
        textureSize = Mathf.Clamp(textureSize, 256, 1024);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= DelayedRegenerate;
        UnityEditor.EditorApplication.delayCall += DelayedRegenerate;
#else
        EnsureRenderer();
        Regenerate();
        FitToCamera(true);
#endif
    }

#if UNITY_EDITOR
    private void DelayedRegenerate()
    {
        UnityEditor.EditorApplication.delayCall -= DelayedRegenerate;
        if (this == null || !isActiveAndEnabled)
            return;

        EnsureRenderer();
        Regenerate();
        FitToCamera(true);
    }
#endif
    private void LateUpdate()
    {
        FitToCamera(false);
    }

    [ContextMenu("Regenerate Grid")]
    public void Regenerate()
    {
        EnsureRenderer();
        ReleaseGeneratedObjects();

        generatedTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false, false)
        {
            name = "Procedural Paper Grid (Generated)",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[textureSize * textureSize];
        float minorSpacing = majorCellPixels / Mathf.Max(1, subdivisions);
        int majorLineCount = Mathf.Max(1, Mathf.RoundToInt(textureSize / majorCellPixels));
        int minorLineCount = Mathf.Max(1, majorLineCount * subdivisions);
        float dashPeriod = Mathf.Max(6f, minorSpacing * 0.34f);

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;

                int majorXIndex = Mathf.RoundToInt(px / majorCellPixels);
                int majorYIndex = Mathf.RoundToInt(py / majorCellPixels);
                float majorXOffset = Wave(py, majorXIndex, majorLineCount, wobble);
                float majorYOffset = Wave(px, majorYIndex + 17, majorLineCount, wobble);
                float majorVertical = LineMask(RepeatDistance(px - majorXOffset, majorCellPixels), majorLineWidth * 0.5f);
                float majorHorizontal = LineMask(RepeatDistance(py - majorYOffset, majorCellPixels), majorLineWidth * 0.5f);
                float majorMask = Mathf.Max(majorVertical, majorHorizontal);

                int minorXIndex = Mathf.RoundToInt(px / minorSpacing);
                int minorYIndex = Mathf.RoundToInt(py / minorSpacing);
                float minorXOffset = Wave(py, minorXIndex, minorLineCount, wobble * 0.55f);
                float minorYOffset = Wave(px, minorYIndex + 31, minorLineCount, wobble * 0.55f);
                float minorVertical = LineMask(RepeatDistance(px - minorXOffset, minorSpacing), minorLineWidth * 0.5f);
                float minorHorizontal = LineMask(RepeatDistance(py - minorYOffset, minorSpacing), minorLineWidth * 0.5f);
                float verticalDash = Mathf.Repeat(py, dashPeriod) < dashPeriod * dashFill ? 1f : 0f;
                float horizontalDash = Mathf.Repeat(px, dashPeriod) < dashPeriod * dashFill ? 1f : 0f;
                float minorMask = Mathf.Max(minorVertical * verticalDash, minorHorizontal * horizontalDash) * minorOpacity;

                float grain = (Hash01(x, y) - 0.5f) * paperGrain;
                Color basePaper = new Color(
                    Mathf.Clamp01(paperColor.r + grain),
                    Mathf.Clamp01(paperColor.g + grain),
                    Mathf.Clamp01(paperColor.b + grain),
                    paperOpacity);
                Color result = Color.Lerp(basePaper, minorColor, minorMask * (1f - majorMask));
                result = Color.Lerp(result, majorColor, majorMask * majorColor.a);
                float lineMask = Mathf.Max(minorMask * minorColor.a, majorMask * majorColor.a);
                result.a = Mathf.Lerp(paperOpacity, 1f, Mathf.Clamp01(lineMask));
                pixels[y * textureSize + x] = result;
            }
        }

        generatedTexture.SetPixels32(pixels);
        generatedTexture.Apply(false, false);
        generatedSprite = Sprite.Create(
            generatedTexture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect);
        generatedSprite.name = "Procedural Paper Grid Sprite (Generated)";
        generatedSprite.hideFlags = HideFlags.HideAndDontSave;

        spriteRenderer.sprite = generatedSprite;
        spriteRenderer.drawMode = SpriteDrawMode.Tiled;
        spriteRenderer.tileMode = SpriteTileMode.Continuous;
        spriteRenderer.color = Color.white;
        spriteRenderer.sortingOrder = -1000;
        FitToCamera(true);
    }

    private void EnsureRenderer()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void FitToCamera(bool force)
    {
        Camera cameraToFit = targetCamera != null ? targetCamera : Camera.main;
        if (cameraToFit == null || !cameraToFit.orthographic || spriteRenderer == null)
            return;

        if (!force && Mathf.Approximately(lastOrthoSize, cameraToFit.orthographicSize) &&
            Mathf.Approximately(lastAspect, cameraToFit.aspect))
            return;

        float height = cameraToFit.orthographicSize * 2f * viewportOverscan;
        float width = height * cameraToFit.aspect;
        spriteRenderer.size = new Vector2(width, height);
        transform.position = new Vector3(cameraToFit.transform.position.x, cameraToFit.transform.position.y, 0f);
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        lastOrthoSize = cameraToFit.orthographicSize;
        lastAspect = cameraToFit.aspect;
    }

    private float Wave(float along, int lineIndex, int lineCount, float strength)
    {
        int wrappedIndex = ((lineIndex % lineCount) + lineCount) % lineCount;
        float phase = wrappedIndex * 1.731f;
        float t = along / textureSize * Mathf.PI * 2f;
        return (Mathf.Sin(t * 3f + phase) * 0.58f +
                Mathf.Sin(t * 7f + phase * 0.37f) * 0.27f +
                Mathf.Sin(t + phase * 0.81f) * 0.15f) * strength;
    }

    private static float RepeatDistance(float value, float spacing)
    {
        return Mathf.Abs(Mathf.Repeat(value + spacing * 0.5f, spacing) - spacing * 0.5f);
    }

    private static float LineMask(float distance, float halfWidth)
    {
        float t = Mathf.InverseLerp(halfWidth, halfWidth + 1.15f, distance);
        float smooth = t * t * (3f - 2f * t);
        return 1f - smooth;
    }

    private static float Hash01(int x, int y)
    {
        unchecked
        {
            uint value = (uint)(x * 374761393 + y * 668265263);
            value = (value ^ (value >> 13)) * 1274126177u;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }

    private void ReleaseGeneratedObjects()
    {
        if (generatedSprite != null)
        {
            if (Application.isPlaying) Destroy(generatedSprite);
            else DestroyImmediate(generatedSprite);
            generatedSprite = null;
        }

        if (generatedTexture != null)
        {
            if (Application.isPlaying) Destroy(generatedTexture);
            else DestroyImmediate(generatedTexture);
            generatedTexture = null;
        }
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= DelayedRegenerate;
#endif
        ReleaseGeneratedObjects();
    }
}
