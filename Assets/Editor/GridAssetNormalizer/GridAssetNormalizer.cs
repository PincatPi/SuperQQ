using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

internal enum GridAssetScaleMode
{
    ContainWithPadding,
    StretchToFill,
    CoverAndCrop
}

internal enum GridAssetPivotPreset
{
    BottomLeft,
    BottomCenter,
    Center,
    Custom
}

[Serializable]
internal sealed class GridAssetImportData
{
    public int version = 3;
    public bool enabled = true;
    public float cellsX = 4f;
    public float cellsY = 4f;
    public int majorCellPixels = 200;
    public int subdivisionsPerMajorCell = 4;
    public float majorCellWorldUnits = 1f;
    public GridAssetScaleMode scaleMode = GridAssetScaleMode.ContainWithPadding;
    public bool trimTransparentBorder = true;
    public float alphaThreshold = 0.01f;
    public GridAssetPivotPreset pivotPreset = GridAssetPivotPreset.Center;
    public Vector2 customPivot = new Vector2(0.5f, 0.5f);
    public FilterMode filterMode = FilterMode.Bilinear;

    // Version 1 used A/B as major-cell counts. Keep these fields only so that
    // previously saved importer metadata can be migrated without changing size.
    [SerializeField] private int pixelsPerCell = 200;
    [SerializeField] private float unitsPerCell = 1f;

    public int SmallCellPixels => Mathf.Max(1, majorCellPixels / subdivisionsPerMajorCell);
    public float SmallCellWorldUnits => majorCellWorldUnits / subdivisionsPerMajorCell;
    public float PixelsPerUnit => majorCellPixels / majorCellWorldUnits;
    public int TargetWidth => Mathf.Max(1, Mathf.RoundToInt(cellsX * SmallCellPixels));
    public int TargetHeight => Mathf.Max(1, Mathf.RoundToInt(cellsY * SmallCellPixels));

    public void Sanitize()
    {
        if (version < 2)
        {
            // Preserve the physical result of old configs: one old A/B cell
            // becomes four minor cells in the corrected 4x4 convention.
            cellsX = Mathf.Max(1f, cellsX) * 4f;
            cellsY = Mathf.Max(1f, cellsY) * 4f;
            majorCellPixels = pixelsPerCell > 0 ? pixelsPerCell : 200;
            majorCellWorldUnits = unitsPerCell > 0f ? unitsPerCell : 1f;
            subdivisionsPerMajorCell = 4;
            version = 2;
        }

        cellsX = RoundToOneDecimal(Mathf.Clamp(cellsX, 0.1f, 64f));
        cellsY = RoundToOneDecimal(Mathf.Clamp(cellsY, 0.1f, 64f));
        subdivisionsPerMajorCell = Mathf.Clamp(subdivisionsPerMajorCell, 1, 16);
        majorCellPixels = Mathf.Clamp(majorCellPixels, subdivisionsPerMajorCell, 2048);
        majorCellPixels = Mathf.Max(
            subdivisionsPerMajorCell,
            Mathf.RoundToInt(majorCellPixels / (float)subdivisionsPerMajorCell) * subdivisionsPerMajorCell);
        majorCellWorldUnits = RoundToOneDecimal(Mathf.Clamp(majorCellWorldUnits, 0.1f, 100f));
        version = 3;
        alphaThreshold = Mathf.Clamp01(alphaThreshold);
        customPivot.x = Mathf.Clamp01(customPivot.x);
        customPivot.y = Mathf.Clamp01(customPivot.y);
    }

    private static float RoundToOneDecimal(float value)
    {
        return Mathf.Round(value * 10f) / 10f;
    }

    public Vector2 GetPivot()
    {
        switch (pivotPreset)
        {
            case GridAssetPivotPreset.BottomLeft:
                return Vector2.zero;
            case GridAssetPivotPreset.BottomCenter:
                return new Vector2(0.5f, 0f);
            case GridAssetPivotPreset.Custom:
                return customPivot;
            default:
                return new Vector2(0.5f, 0.5f);
        }
    }
}

internal static class GridAssetImportMetadata
{
    private const string Marker = "GRID_ASSET_NORMALIZER:";
    private static readonly Regex FileNamePattern = new Regex(
        @"(?:_|-)(\d+(?:\.\d)?)[xX×](\d+(?:\.\d)?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryRead(TextureImporter importer, out GridAssetImportData data)
    {
        data = null;
        if (importer == null || string.IsNullOrEmpty(importer.userData))
            return false;

        string[] lines = importer.userData.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(Marker, StringComparison.Ordinal))
                continue;

            string json = lines[i].Substring(Marker.Length);
            try
            {
                data = JsonUtility.FromJson<GridAssetImportData>(json);
                if (data == null)
                    return false;
                data.Sanitize();
                return data.enabled;
            }
            catch (Exception)
            {
                return false;
            }
        }

        return false;
    }

    public static bool TryReadOrInfer(TextureImporter importer, string assetPath, out GridAssetImportData data)
    {
        if (TryRead(importer, out data))
            return true;

        string normalizedPath = assetPath.Replace('\\', '/');
        bool isGridItemsFolder = normalizedPath.StartsWith("Assets/GridItems/", StringComparison.OrdinalIgnoreCase) ||
                                 normalizedPath.IndexOf("/GridItems/", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!isGridItemsFolder)
            return false;

        Match match = FileNamePattern.Match(Path.GetFileNameWithoutExtension(normalizedPath));
        if (!match.Success ||
            !float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float cellsX) ||
            !float.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float cellsY))
            return false;

        data = new GridAssetImportData
        {
            cellsX = cellsX,
            cellsY = cellsY
        };
        data.Sanitize();
        return true;
    }

    public static string Write(string existingUserData, GridAssetImportData data)
    {
        string cleaned = Remove(existingUserData);
        string entry = Marker + JsonUtility.ToJson(data);
        return string.IsNullOrEmpty(cleaned) ? entry : cleaned + "\n" + entry;
    }

    public static string Remove(string existingUserData)
    {
        if (string.IsNullOrEmpty(existingUserData))
            return string.Empty;

        string[] lines = existingUserData.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines.Where(line => !line.StartsWith(Marker, StringComparison.Ordinal))).Trim();
    }
}

internal sealed class GridAssetNormalizerPostprocessor : AssetPostprocessor
{
    public override uint GetVersion()
    {
        return 3;
    }

    private void OnPreprocessTexture()
    {
        TextureImporter importer = assetImporter as TextureImporter;
        if (!GridAssetImportMetadata.TryReadOrInfer(importer, assetPath, out GridAssetImportData data))
            return;

        data.Sanitize();
        Vector2 pivot = data.GetPivot();
        int targetWidth = data.TargetWidth;
        int targetHeight = data.TargetHeight;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = data.PixelsPerUnit;
        importer.spritePivot = pivot;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = data.filterMode;
        importer.crunchedCompression = false;
        importer.maxTextureSize = Mathf.Clamp(
            Mathf.NextPowerOfTwo(Mathf.Max(targetWidth, targetHeight)),
            32,
            16384);

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMode = (int)SpriteImportMode.Single;
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivot;
        settings.spritePixelsPerUnit = data.PixelsPerUnit;
        importer.SetTextureSettings(settings);
    }

    private void OnPostprocessTexture(Texture2D texture)
    {
        TextureImporter importer = assetImporter as TextureImporter;
        if (!GridAssetImportMetadata.TryReadOrInfer(importer, assetPath, out GridAssetImportData data))
            return;

        data.Sanitize();
        int targetWidth = data.TargetWidth;
        int targetHeight = data.TargetHeight;
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            context.LogImportError("网格资产目标尺寸无效：" + assetPath);
            return;
        }

        Color32[] sourcePixels = texture.GetPixels32();
        int sourceWidth = texture.width;
        int sourceHeight = texture.height;
        RectInt sourceRect = data.trimTransparentBorder
            ? FindContentRect(sourcePixels, sourceWidth, sourceHeight, data.alphaThreshold)
            : new RectInt(0, 0, sourceWidth, sourceHeight);

        Color32[] result = Resample(
            sourcePixels,
            sourceWidth,
            sourceHeight,
            sourceRect,
            targetWidth,
            targetHeight,
            data);

        if (!texture.Reinitialize(targetWidth, targetHeight, TextureFormat.RGBA32, false))
        {
            context.LogImportError("无法重建网格资产纹理，请检查纹理格式或压缩设置：" + assetPath);
            return;
        }

        texture.SetPixels32(result);
        texture.Apply(false, false);
    }

    private static RectInt FindContentRect(Color32[] pixels, int width, int height, float alphaThreshold)
    {
        byte threshold = (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaThreshold) * 255f);
        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (pixels[row + x].a <= threshold)
                    continue;

                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
        }

        return maxX < minX || maxY < minY
            ? new RectInt(0, 0, width, height)
            : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Color32[] Resample(
        Color32[] source,
        int sourceWidth,
        int sourceHeight,
        RectInt sourceRect,
        int targetWidth,
        int targetHeight,
        GridAssetImportData data)
    {
        int scaledWidth;
        int scaledHeight;

        if (data.scaleMode == GridAssetScaleMode.StretchToFill)
        {
            scaledWidth = targetWidth;
            scaledHeight = targetHeight;
        }
        else
        {
            float scaleX = targetWidth / (float)sourceRect.width;
            float scaleY = targetHeight / (float)sourceRect.height;
            float scale = data.scaleMode == GridAssetScaleMode.CoverAndCrop
                ? Mathf.Max(scaleX, scaleY)
                : Mathf.Min(scaleX, scaleY);
            scaledWidth = Mathf.Max(1, Mathf.RoundToInt(sourceRect.width * scale));
            scaledHeight = Mathf.Max(1, Mathf.RoundToInt(sourceRect.height * scale));
        }

        Vector2 pivot = data.GetPivot();
        int offsetX = Mathf.RoundToInt((targetWidth - scaledWidth) * pivot.x);
        int offsetY = Mathf.RoundToInt((targetHeight - scaledHeight) * pivot.y);
        Color32[] output = new Color32[targetWidth * targetHeight];

        for (int y = 0; y < targetHeight; y++)
        {
            float v = (y - offsetY + 0.5f) / scaledHeight;
            if (v < 0f || v >= 1f)
                continue;

            for (int x = 0; x < targetWidth; x++)
            {
                float u = (x - offsetX + 0.5f) / scaledWidth;
                if (u < 0f || u >= 1f)
                    continue;

                output[y * targetWidth + x] = data.filterMode == FilterMode.Point
                    ? SamplePoint(source, sourceWidth, sourceHeight, sourceRect, u, v)
                    : SampleBilinear(source, sourceWidth, sourceHeight, sourceRect, u, v);
            }
        }

        return output;
    }

    private static Color32 SamplePoint(
        Color32[] source,
        int width,
        int height,
        RectInt rect,
        float u,
        float v)
    {
        int x = Mathf.Clamp(rect.xMin + Mathf.FloorToInt(u * rect.width), rect.xMin, rect.xMax - 1);
        int y = Mathf.Clamp(rect.yMin + Mathf.FloorToInt(v * rect.height), rect.yMin, rect.yMax - 1);
        x = Mathf.Clamp(x, 0, width - 1);
        y = Mathf.Clamp(y, 0, height - 1);
        return source[y * width + x];
    }

    private static Color32 SampleBilinear(
        Color32[] source,
        int width,
        int height,
        RectInt rect,
        float u,
        float v)
    {
        float sourceX = rect.xMin + u * Mathf.Max(0, rect.width - 1);
        float sourceY = rect.yMin + v * Mathf.Max(0, rect.height - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(sourceX), 0, width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(sourceY), 0, height - 1);
        int x1 = Mathf.Clamp(x0 + 1, 0, width - 1);
        int y1 = Mathf.Clamp(y0 + 1, 0, height - 1);
        float tx = sourceX - x0;
        float ty = sourceY - y0;

        Color bottom = Color.Lerp(source[y0 * width + x0], source[y0 * width + x1], tx);
        Color top = Color.Lerp(source[y1 * width + x0], source[y1 * width + x1], tx);
        return Color.Lerp(bottom, top, ty);
    }
}

internal sealed class GridAssetNormalizerWindow : EditorWindow
{
    private readonly GridAssetImportData settings = new GridAssetImportData();
    private Vector2 scrollPosition;

    [MenuItem("Tools/网格资产规范器")]
    private static void Open()
    {
        GridAssetNormalizerWindow window = GetWindow<GridAssetNormalizerWindow>();
        window.titleContent = new GUIContent("网格资产规范器");
        window.minSize = new Vector2(430f, 560f);
        window.Show();
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnGUI()
    {
        Texture2D[] textures = GetSelectedTextures();
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("网格资产规范器", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "原始图片不会被修改。A、B 表示小格数量；默认一个大格固定包含 4×4 个小格。",
            MessageType.Info);

        DrawSelection(textures);
        EditorGUILayout.Space(8f);

        EditorGUILayout.LabelField("目标网格", EditorStyles.boldLabel);
        settings.cellsX = EditorGUILayout.FloatField("宽度 A（小格）", settings.cellsX);
        settings.cellsY = EditorGUILayout.FloatField("高度 B（小格）", settings.cellsY);
        settings.majorCellPixels = EditorGUILayout.IntSlider("每大格像素", settings.majorCellPixels, 16, 2048);
        settings.subdivisionsPerMajorCell = EditorGUILayout.IntSlider(
            "每大格细分",
            settings.subdivisionsPerMajorCell,
            1,
            16);
        settings.majorCellWorldUnits = EditorGUILayout.FloatField("每大格世界单位", settings.majorCellWorldUnits);
        settings.Sanitize();

        int targetWidth = settings.TargetWidth;
        int targetHeight = settings.TargetHeight;
        EditorGUILayout.HelpBox(
            string.Format(
                "每小格：{0} px / {1:0.###} Unity Units\n导入尺寸：{2}×{3} px    Sprite 世界尺寸：{4:0.###}×{5:0.###}",
                settings.SmallCellPixels,
                settings.SmallCellWorldUnits,
                targetWidth,
                targetHeight,
                settings.cellsX * settings.SmallCellWorldUnits,
                settings.cellsY * settings.SmallCellWorldUnits),
            MessageType.None);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("修订方式", EditorStyles.boldLabel);
        settings.scaleMode = (GridAssetScaleMode)EditorGUILayout.EnumPopup("比例处理", settings.scaleMode);
        settings.trimTransparentBorder = EditorGUILayout.Toggle("裁掉透明空边", settings.trimTransparentBorder);
        using (new EditorGUI.DisabledScope(!settings.trimTransparentBorder))
            settings.alphaThreshold = EditorGUILayout.Slider("透明裁边阈值", settings.alphaThreshold, 0f, 0.25f);
        settings.filterMode = (FilterMode)EditorGUILayout.EnumPopup("缩放采样", settings.filterMode);

        if (settings.scaleMode == GridAssetScaleMode.StretchToFill)
            EditorGUILayout.HelpBox("拉伸填满会改变素材原始比例。", MessageType.Warning);
        else if (settings.scaleMode == GridAssetScaleMode.CoverAndCrop)
            EditorGUILayout.HelpBox("等比裁切会截掉目标格子范围外的图像。", MessageType.Warning);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("放置基准", EditorStyles.boldLabel);
        settings.pivotPreset = (GridAssetPivotPreset)EditorGUILayout.EnumPopup("Pivot", settings.pivotPreset);
        if (settings.pivotPreset == GridAssetPivotPreset.Custom)
            settings.customPivot = EditorGUILayout.Vector2Field("自定义 Pivot", settings.customPivot);

        settings.Sanitize();
        EditorGUILayout.Space(12f);

        using (new EditorGUI.DisabledScope(textures.Length == 0))
        {
            if (GUILayout.Button("应用到所选素材并重新导入", GUILayout.Height(34f)))
                ApplyToSelection(textures);

            if (GUILayout.Button("读取首个所选素材的配置"))
                LoadFromFirst(textures);

            if (GUILayout.Button("移除规范并恢复原始导入结果"))
                RemoveFromSelection(textures);
        }

        EditorGUILayout.Space(12f);
        EditorGUILayout.HelpBox(
            "自动导入：把图片放进 Assets/GridItems，并按小格数命名。名称_4x4.png 等于一个大格，也支持名称_4.5x2.0.png。尺寸单位保留一位小数。",
            MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private static void DrawSelection(Texture2D[] textures)
    {
        EditorGUILayout.LabelField("当前选择", EditorStyles.boldLabel);
        if (textures.Length == 0)
        {
            EditorGUILayout.HelpBox("请在 Project 窗口选择一个或多个图片资源。", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("已选择 " + textures.Length + " 个图片资源");
        int previewCount = Mathf.Min(6, textures.Length);
        for (int i = 0; i < previewCount; i++)
        {
            string path = AssetDatabase.GetAssetPath(textures[i]);
            EditorGUILayout.LabelField(
                "• " + path + "  (" + textures[i].width + "×" + textures[i].height + ")",
                EditorStyles.miniLabel);
        }

        if (textures.Length > previewCount)
            EditorGUILayout.LabelField("…以及另外 " + (textures.Length - previewCount) + " 个", EditorStyles.miniLabel);
    }

    private static Texture2D[] GetSelectedTextures()
    {
        return Selection.GetFiltered<Texture2D>(SelectionMode.Assets | SelectionMode.DeepAssets)
            .Where(texture => AssetDatabase.GetAssetPath(texture).StartsWith("Assets/", StringComparison.Ordinal))
            .Distinct()
            .ToArray();
    }

    private void ApplyToSelection(IReadOnlyList<Texture2D> textures)
    {
        settings.Sanitize();
        try
        {
            for (int i = 0; i < textures.Count; i++)
            {
                string path = AssetDatabase.GetAssetPath(textures[i]);
                EditorUtility.DisplayProgressBar(
                    "规范网格资产",
                    path,
                    (i + 1f) / textures.Count);

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                importer.userData = GridAssetImportMetadata.Write(importer.userData, settings);
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void LoadFromFirst(IReadOnlyList<Texture2D> textures)
    {
        if (textures.Count == 0)
            return;

        string path = AssetDatabase.GetAssetPath(textures[0]);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!GridAssetImportMetadata.TryRead(importer, out GridAssetImportData imported))
        {
            ShowNotification(new GUIContent("该素材还没有手动规范配置"));
            return;
        }

        settings.cellsX = imported.cellsX;
        settings.cellsY = imported.cellsY;
        settings.majorCellPixels = imported.majorCellPixels;
        settings.subdivisionsPerMajorCell = imported.subdivisionsPerMajorCell;
        settings.majorCellWorldUnits = imported.majorCellWorldUnits;
        settings.scaleMode = imported.scaleMode;
        settings.trimTransparentBorder = imported.trimTransparentBorder;
        settings.alphaThreshold = imported.alphaThreshold;
        settings.pivotPreset = imported.pivotPreset;
        settings.customPivot = imported.customPivot;
        settings.filterMode = imported.filterMode;
        Repaint();
    }

    private static void RemoveFromSelection(IReadOnlyList<Texture2D> textures)
    {
        if (!EditorUtility.DisplayDialog(
                "移除网格规范",
                "这会移除所选素材的规范配置并从原始图片重新导入，不会删除原始文件。",
                "移除并重新导入",
                "取消"))
            return;

        try
        {
            for (int i = 0; i < textures.Count; i++)
            {
                string path = AssetDatabase.GetAssetPath(textures[i]);
                EditorUtility.DisplayProgressBar(
                    "恢复原始导入结果",
                    path,
                    (i + 1f) / textures.Count);

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                importer.userData = GridAssetImportMetadata.Remove(importer.userData);
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
