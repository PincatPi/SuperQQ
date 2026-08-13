using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class ProceduralPaperGrid : MonoBehaviour
{
    [Header("Grid Structure")]
    [SerializeField, Min(32f)] private float majorCellPixels = 128f;
    [SerializeField, Range(3, 8)] private int subdivisions = 5;

    [Header("Paper Size / 纸张尺寸")]
    [Tooltip("关闭后使用自定义纸张尺寸；开启后保持原来的铺满相机行为。")]
    [InspectorName("铺满相机")]
    [SerializeField] private bool fitToCamera;
    [Tooltip("纸张宽度，以大格为单位。支持一位小数，例如 3.5。")]
    [InspectorName("纸张宽度（大格）")]
    [SerializeField, Min(0.5f)] private float paperWidthInMajorCells = 3.5f;
    [Tooltip("纸张高度，以大格为单位。支持一位小数，例如 2.5。")]
    [InspectorName("纸张高度（大格）")]
    [SerializeField, Min(0.5f)] private float paperHeightInMajorCells = 2.5f;
    [Tooltip("尺寸含小数时，把不足一个大格的部分留在左侧/下侧，效果与参考图一致。")]
    [InspectorName("半格放在左侧/下侧")]
    [SerializeField] private bool alignPartialCellToLeftAndBottom = true;

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
    }

    private void OnValidate()
    {
        majorCellPixels = Mathf.Max(32f, majorCellPixels);
        subdivisions = Mathf.Clamp(subdivisions, 3, 8);
        paperWidthInMajorCells = RoundToOneDecimal(Mathf.Max(0.5f, paperWidthInMajorCells));
        paperHeightInMajorCells = RoundToOneDecimal(Mathf.Max(0.5f, paperHeightInMajorCells));
        textureSize = Mathf.Clamp(textureSize, 256, 1024);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= DelayedRegenerate;
        UnityEditor.EditorApplication.delayCall += DelayedRegenerate;
#else
        EnsureRenderer();
        Regenerate();
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
    }
#endif
    private void LateUpdate()
    {
        if (fitToCamera)
            FitToCamera(false);
    }

    [ContextMenu("Regenerate Grid")]
    public void Regenerate()
    {
        EnsureRenderer();
        ReleaseGeneratedObjects();

        int outputWidth = fitToCamera
            ? textureSize
            : Mathf.Clamp(Mathf.RoundToInt(paperWidthInMajorCells * majorCellPixels), 1, 8192);
        int outputHeight = fitToCamera
            ? textureSize
            : Mathf.Clamp(Mathf.RoundToInt(paperHeightInMajorCells * majorCellPixels), 1, 8192);

        generatedTexture = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false, false)
        {
            name = "Procedural Paper Grid (Generated)",
            wrapMode = fitToCamera ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[outputWidth * outputHeight];
        float minorSpacing = majorCellPixels / Mathf.Max(1, subdivisions);
        int majorLineCountX = Mathf.Max(1, Mathf.CeilToInt(outputWidth / majorCellPixels));
        int majorLineCountY = Mathf.Max(1, Mathf.CeilToInt(outputHeight / majorCellPixels));
        int minorLineCountX = Mathf.Max(1, majorLineCountX * subdivisions);
        int minorLineCountY = Mathf.Max(1, majorLineCountY * subdivisions);
        float dashPeriod = Mathf.Max(6f, minorSpacing * 0.34f);
        float majorPhaseX = GetGridPhase(
            fitToCamera ? 0f : paperWidthInMajorCells,
            majorCellPixels,
            alignPartialCellToLeftAndBottom);
        float majorPhaseY = GetGridPhase(
            fitToCamera ? 0f : paperHeightInMajorCells,
            majorCellPixels,
            alignPartialCellToLeftAndBottom);
        float minorPhaseX = Mathf.Repeat(majorPhaseX, minorSpacing);
        float minorPhaseY = Mathf.Repeat(majorPhaseY, minorSpacing);

        for (int y = 0; y < outputHeight; y++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;

                int majorXIndex = Mathf.RoundToInt((px - majorPhaseX) / majorCellPixels);
                int majorYIndex = Mathf.RoundToInt((py - majorPhaseY) / majorCellPixels);
                float majorXOffset = Wave(py, majorXIndex, majorLineCountX, wobble, outputHeight);
                float majorYOffset = Wave(px, majorYIndex + 17, majorLineCountY, wobble, outputWidth);
                float majorVertical = LineMask(
                    RepeatDistance(px - majorPhaseX - majorXOffset, majorCellPixels),
                    majorLineWidth * 0.5f);
                float majorHorizontal = LineMask(
                    RepeatDistance(py - majorPhaseY - majorYOffset, majorCellPixels),
                    majorLineWidth * 0.5f);
                float majorMask = Mathf.Max(majorVertical, majorHorizontal);

                int minorXIndex = Mathf.RoundToInt((px - minorPhaseX) / minorSpacing);
                int minorYIndex = Mathf.RoundToInt((py - minorPhaseY) / minorSpacing);
                float minorXOffset = Wave(py, minorXIndex, minorLineCountX, wobble * 0.55f, outputHeight);
                float minorYOffset = Wave(px, minorYIndex + 31, minorLineCountY, wobble * 0.55f, outputWidth);
                float minorVertical = LineMask(
                    RepeatDistance(px - minorPhaseX - minorXOffset, minorSpacing),
                    minorLineWidth * 0.5f);
                float minorHorizontal = LineMask(
                    RepeatDistance(py - minorPhaseY - minorYOffset, minorSpacing),
                    minorLineWidth * 0.5f);
                float verticalDash = Mathf.Repeat(py, dashPeriod) < dashPeriod * dashFill ? 1f : 0f;
                float horizontalDash = Mathf.Repeat(px, dashPeriod) < dashPeriod * dashFill ? 1f : 0f;
                float minorMask = Mathf.Max(minorVertical * verticalDash, minorHorizontal * horizontalDash) * minorOpacity;

                if (!fitToCamera)
                {
                    float edgeDistance = Mathf.Min(
                        Mathf.Min(px, outputWidth - px),
                        Mathf.Min(py, outputHeight - py));
                    float borderMask = LineMask(edgeDistance, majorLineWidth * 0.5f);
                    majorMask = Mathf.Max(majorMask, borderMask);
                }

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
                pixels[y * outputWidth + x] = result;
            }
        }

        generatedTexture.SetPixels32(pixels);
        generatedTexture.Apply(false, false);
        generatedSprite = Sprite.Create(
            generatedTexture,
            new Rect(0f, 0f, outputWidth, outputHeight),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect);
        generatedSprite.name = "Procedural Paper Grid Sprite (Generated)";
        generatedSprite.hideFlags = HideFlags.HideAndDontSave;

        spriteRenderer.sprite = generatedSprite;
        spriteRenderer.drawMode = fitToCamera ? SpriteDrawMode.Tiled : SpriteDrawMode.Simple;
        if (fitToCamera)
            spriteRenderer.tileMode = SpriteTileMode.Continuous;
        spriteRenderer.color = Color.white;
        spriteRenderer.sortingOrder = -1000;
        if (fitToCamera)
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

    private static float Wave(float along, int lineIndex, int lineCount, float strength, float periodPixels)
    {
        int wrappedIndex = ((lineIndex % lineCount) + lineCount) % lineCount;
        float phase = wrappedIndex * 1.731f;
        float t = along / Mathf.Max(1f, periodPixels) * Mathf.PI * 2f;
        return (Mathf.Sin(t * 3f + phase) * 0.58f +
                Mathf.Sin(t * 7f + phase * 0.37f) * 0.27f +
                Mathf.Sin(t + phase * 0.81f) * 0.15f) * strength;
    }

    private static float RepeatDistance(float value, float spacing)
    {
        return Mathf.Abs(Mathf.Repeat(value + spacing * 0.5f, spacing) - spacing * 0.5f);
    }

    private static float GetGridPhase(float sizeInMajorCells, float spacing, bool putPartialCellAtNearEdge)
    {
        if (!putPartialCellAtNearEdge)
            return 0f;

        float fractionalCell = Mathf.Repeat(sizeInMajorCells, 1f);
        return fractionalCell < 0.001f || 1f - fractionalCell < 0.001f
            ? 0f
            : fractionalCell * spacing;
    }

    private static float RoundToOneDecimal(float value)
    {
        return Mathf.Round(value * 10f) / 10f;
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
