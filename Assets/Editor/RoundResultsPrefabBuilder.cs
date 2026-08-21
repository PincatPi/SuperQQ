using System.IO;
using SuperQQ.UI.RoundResults;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.EditorTools
{
    public static class RoundResultsPrefabBuilder
    {
        private const string OutputFolder = "Assets/Prefab/UI/RoundResults";
        private const string ArtFolder = "Assets/Art/UI/RoundResults";
        private const string RowPrefabPath = OutputFolder + "/RoundResultRow.prefab";
        private const string PanelPrefabPath = OutputFolder + "/RoundResultsPanel.prefab";
        private const string RoundedSpritePath = ArtFolder + "/ui_rounded_rect.png";
        private const string CircleSpritePath = ArtFolder + "/ui_circle.png";
        private const string HatchSpritePath = ArtFolder + "/ui_hand_drawn_hatch.png";
        private const string FontPath = "Assets/GUIPackCartoon/Demo/Fonts/LilitaOne - Regular SDF.asset";

        private static TMP_FontAsset _font;
        private static Sprite _roundedSprite;
        private static Sprite _circleSprite;
        private static Sprite _hatchSprite;

        [MenuItem("Tools/SuperQQ/UI/Build Round Results Prefabs")]
        public static void Build()
        {
            Directory.CreateDirectory(OutputFolder);
            Directory.CreateDirectory(ArtFolder);

            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            CreateShapeAssets();
            _roundedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);
            _circleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);
            _hatchSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HatchSpritePath);

            RoundResultRowView rowPrefab = BuildRowPrefab();
            BuildPanelPrefab(rowPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UnityEditor.Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            EditorGUIUtility.PingObject(UnityEditor.Selection.activeObject);
            Debug.Log($"[RoundResultsPrefabBuilder] Built {PanelPrefabPath} and {RowPrefabPath}");
        }

        private static RoundResultRowView BuildRowPrefab()
        {
            GameObject root = NewUIObject("RoundResultRow", null);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(1030f, 92f);

            CanvasGroup canvasGroup = root.AddComponent<CanvasGroup>();
            Image background = root.AddComponent<Image>();
            background.sprite = _roundedSprite;
            background.type = Image.Type.Sliced;
            background.color = new Color32(255, 251, 230, 248);
            background.raycastTarget = false;

            Outline outline = root.AddComponent<Outline>();
            outline.effectColor = new Color32(43, 50, 91, 210);
            outline.effectDistance = new Vector2(3f, -3f);

            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = 92f;
            layout.flexibleWidth = 1f;

            TMP_Text rankText = AddText(root.transform, "Rank", "1", 34f, FontStyles.Bold, TextAlignmentOptions.Center);
            Place(rankText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(52f, 64f));
            rankText.color = new Color32(48, 52, 89, 255);

            Image avatar = AddImage(root.transform, "Avatar", _circleSprite, new Color32(75, 184, 173, 255), Image.Type.Simple);
            Place(avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(91f, 0f), new Vector2(62f, 62f));

            TMP_Text avatarInitial = AddText(avatar.transform, "Initial", "P", 30f, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(avatarInitial.rectTransform, 0f, 0f, 0f, 0f);
            avatarInitial.color = Color.white;

            TMP_Text playerName = AddText(root.transform, "PlayerName", "PLAYER", 25f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Place(playerName.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(235f, 6f), new Vector2(176f, 36f));
            playerName.color = new Color32(48, 52, 89, 255);

            TMP_Text winnerText = null;
            GameObject winnerBadge = NewUIObject("WinnerBadge", root.transform);
            Image winnerImage = winnerBadge.AddComponent<Image>();
            winnerImage.sprite = _roundedSprite;
            winnerImage.type = Image.Type.Sliced;
            winnerImage.color = new Color32(244, 171, 48, 255);
            Place(winnerBadge.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(205f, -25f), new Vector2(112f, 25f));
            winnerText = AddText(winnerBadge.transform, "Label", "WINNER", 14f, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(winnerText.rectTransform, 4f, 2f, 4f, 2f);
            winnerText.color = new Color32(57, 47, 68, 255);

            Image track = AddImage(
                root.transform,
                "ScoreTrack",
                _roundedSprite,
                new Color32(232, 226, 202, 255),
                Image.Type.Sliced);
            Place(
                track.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(588f, 0f),
                new Vector2(510f, 64f));

            Outline trackOutline = track.gameObject.AddComponent<Outline>();
            trackOutline.effectColor = new Color32(67, 73, 119, 205);
            trackOutline.effectDistance = new Vector2(2f, -2f);

            GameObject fillContentObject = NewUIObject("FillContent", track.transform);
            RectTransform fillContent = fillContentObject.GetComponent<RectTransform>();
            Stretch(fillContent, 8f, 8f, 8f, 8f);

            Image previousFill = AddImage(
                fillContent,
                "PreviousScore",
                _roundedSprite,
                new Color32(226, 226, 207, 255),
                Image.Type.Sliced);
            previousFill.pixelsPerUnitMultiplier = 2f;
            SetHorizontalAnchors(previousFill.rectTransform, 0f, 0.5f);

            Outline previousOutline = previousFill.gameObject.AddComponent<Outline>();
            previousOutline.effectColor = new Color32(56, 70, 133, 245);
            previousOutline.effectDistance = new Vector2(1.5f, -1.5f);
            previousOutline.useGraphicAlpha = true;

            Mask previousMask = previousFill.gameObject.AddComponent<Mask>();
            previousMask.showMaskGraphic = true;

            Image previousHatch = AddImage(
                previousFill.transform,
                "HandDrawnHatch",
                _hatchSprite,
                new Color32(56, 70, 133, 228),
                Image.Type.Tiled);
            Stretch(previousHatch.rectTransform, 0f, 0f, 0f, 0f);

            GameObject segmentRootObject = NewUIObject("RoundSegments", fillContent);
            RectTransform segmentRoot = segmentRootObject.GetComponent<RectTransform>();
            Stretch(segmentRoot, 0f, 0f, 0f, 0f);

            TMP_Text scoreText = AddText(root.transform, "TotalScore", "45 / 100", 24f, FontStyles.Bold, TextAlignmentOptions.Center);
            Place(scoreText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(895f, 8f), new Vector2(110f, 34f));
            scoreText.color = new Color32(48, 52, 89, 255);

            TMP_Text deltaText = AddText(root.transform, "RoundDelta", "+20", 20f, FontStyles.Bold, TextAlignmentOptions.Center);
            Place(deltaText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(895f, -22f), new Vector2(100f, 26f));
            deltaText.color = new Color32(35, 153, 112, 255);

            RoundResultRowView view = root.AddComponent<RoundResultRowView>();
            view.Configure(
                canvasGroup,
                rootRect,
                rankText,
                avatar,
                avatarInitial,
                playerName,
                scoreText,
                deltaText,
                fillContent,
                previousFill,
                previousHatch,
                segmentRoot,
                winnerBadge,
                winnerText);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, RowPrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<RoundResultRowView>();
        }

        private static void BuildPanelPrefab(RoundResultRowView rowPrefab)
        {
            GameObject root = NewUIObject("RoundResultsPanel", null);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            CanvasGroup canvasGroup = root.AddComponent<CanvasGroup>();
            Image dimmer = root.AddComponent<Image>();
            dimmer.color = new Color32(20, 30, 55, 226);
            dimmer.raycastTarget = true;

            GameObject boardObject = NewUIObject("PaperBoard", root.transform);
            RectTransform board = boardObject.GetComponent<RectTransform>();
            Place(board, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1120f, 650f));
            Image boardImage = boardObject.AddComponent<Image>();
            boardImage.sprite = _roundedSprite;
            boardImage.type = Image.Type.Sliced;
            boardImage.color = new Color32(246, 239, 207, 255);
            Outline boardOutline = boardObject.AddComponent<Outline>();
            boardOutline.effectColor = new Color32(47, 54, 93, 255);
            boardOutline.effectDistance = new Vector2(7f, -7f);

            AddCornerAccent(board, "AccentTopLeft", new Vector2(-500f, 286f), new Color32(72, 191, 181, 255), 11f);
            AddCornerAccent(board, "AccentTopRight", new Vector2(500f, 286f), new Color32(236, 102, 83, 255), -11f);

            TMP_Text title = AddText(board, "Title", "ROUND RESULTS", 50f, FontStyles.Bold, TextAlignmentOptions.Center);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -54f), new Vector2(700f, 72f));
            title.color = new Color32(47, 54, 93, 255);

            TMP_Text subtitle = AddText(board, "RoundLabel", "ROUND 1", 22f, FontStyles.Bold, TextAlignmentOptions.Center);
            Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -101f), new Vector2(230f, 36f));
            subtitle.color = new Color32(94, 99, 129, 255);

            GameObject goalChip = NewUIObject("GoalChip", board);
            Image goalChipImage = goalChip.AddComponent<Image>();
            goalChipImage.sprite = _roundedSprite;
            goalChipImage.type = Image.Type.Sliced;
            goalChipImage.color = new Color32(47, 54, 93, 255);
            Place(goalChip.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-116f, -55f), new Vector2(170f, 46f));
            TMP_Text victoryLine = AddText(goalChip.transform, "GoalText", "GOAL 100", 20f, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(victoryLine.rectTransform, 6f, 3f, 6f, 3f);
            victoryLine.color = new Color32(246, 239, 207, 255);

            GameObject rowsObject = NewUIObject("Rows", board);
            RectTransform rowsRoot = rowsObject.GetComponent<RectTransform>();
            Place(rowsRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(1030f, 414f));
            VerticalLayoutGroup vertical = rowsObject.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = 12f;
            vertical.childAlignment = TextAnchor.UpperCenter;
            vertical.childControlWidth = true;
            vertical.childControlHeight = false;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;

            CreateLegend(board);

            Button continueButton = CreateButton(board, "ContinueButton", "CONTINUE", new Color32(236, 102, 83, 255), out TMP_Text continueText);
            Place(continueButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-135f, 45f), new Vector2(220f, 58f));

            TMP_Text hint = AddText(board, "Hint", "Progress is cumulative  •  colored blocks are this round", 15f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(310f, 45f), new Vector2(540f, 32f));
            hint.color = new Color32(94, 99, 129, 255);

            RoundResultsPanel panel = root.AddComponent<RoundResultsPanel>();
            panel.Configure(canvasGroup, board, title, subtitle, victoryLine, rowsRoot, rowPrefab, continueButton, continueText);

            root.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void CreateLegend(Transform board)
        {
            string[] labels = { "FINISH", "1ST", "SOLO", "TRAP", "SPECIAL", "ITEM" };
            Color32[] colors =
            {
                new(41, 185, 172, 255),
                new(245, 174, 54, 255),
                new(235, 95, 91, 255),
                new(226, 73, 110, 255),
                new(145, 103, 214, 255),
                new(87, 190, 105, 255)
            };

            GameObject legend = NewUIObject("Legend", board);
            RectTransform legendRect = legend.GetComponent<RectTransform>();
            Place(legendRect, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(302f, 87f), new Vector2(544f, 34f));
            HorizontalLayoutGroup layout = legend.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 7f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            for (int i = 0; i < labels.Length; i++)
            {
                GameObject chip = NewUIObject(labels[i], legend.transform);
                Image image = chip.AddComponent<Image>();
                image.sprite = _roundedSprite;
                image.type = Image.Type.Sliced;
                image.color = colors[i];
                LayoutElement element = chip.AddComponent<LayoutElement>();
                element.preferredWidth = labels[i] == "SPECIAL" ? 86f : 70f;
                TMP_Text text = AddText(chip.transform, "Text", labels[i], 12f, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(text.rectTransform, 3f, 1f, 3f, 1f);
                text.color = Color.white;
            }
        }

        private static Button CreateButton(Transform parent, string name, string label, Color color, out TMP_Text text)
        {
            GameObject buttonObject = NewUIObject(name, parent);
            Image image = buttonObject.AddComponent<Image>();
            image.sprite = _roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = color;

            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = new Color32(47, 54, 93, 255);
            outline.effectDistance = new Vector2(4f, -4f);

            Button button = buttonObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.16f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.15f);
            colors.disabledColor = new Color(color.r, color.g, color.b, 0.42f);
            button.colors = colors;

            text = AddText(buttonObject.transform, "Text", label, 23f, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 8f, 4f, 8f, 4f);
            text.color = Color.white;
            return button;
        }

        private static void AddCornerAccent(Transform board, string name, Vector2 position, Color color, float rotation)
        {
            Image accent = AddImage(board, name, _roundedSprite, color, Image.Type.Sliced);
            Place(accent.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(130f, 24f));
            accent.rectTransform.localEulerAngles = new Vector3(0f, 0f, rotation);
            accent.raycastTarget = false;
        }

        private static TMP_Text AddText(Transform parent, string name, string value, float fontSize, FontStyles style, TextAlignmentOptions alignment)
        {
            GameObject textObject = NewUIObject(name, parent);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            if (_font != null)
            {
                text.font = _font;
            }
            return text;
        }

        private static Image AddImage(Transform parent, string name, Sprite sprite, Color color, Image.Type type)
        {
            GameObject imageObject = NewUIObject(name, parent);
            Image image = imageObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = type;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static GameObject NewUIObject(string name, Transform parent)
        {
            GameObject gameObject = new(name, typeof(RectTransform));
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }
            return gameObject;
        }

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void SetHorizontalAnchors(RectTransform rect, float minX, float maxX)
        {
            rect.anchorMin = new Vector2(minX, 0f);
            rect.anchorMax = new Vector2(maxX, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void CreateShapeAssets()
        {
            if (!File.Exists(ToAbsolutePath(RoundedSpritePath)))
            {
                WriteRoundedRect(RoundedSpritePath, 64, 14f);
            }

            if (!File.Exists(ToAbsolutePath(CircleSpritePath)))
            {
                WriteCircle(CircleSpritePath, 64);
            }

            if (!File.Exists(ToAbsolutePath(HatchSpritePath)))
            {
                WriteDiagonalHatch(HatchSpritePath, 48);
            }

            ConfigureSpriteImporter(RoundedSpritePath, new Vector4(16f, 16f, 16f, 16f));
            ConfigureSpriteImporter(CircleSpritePath, Vector4.zero);
            ConfigureSpriteImporter(HatchSpritePath, Vector4.zero);
        }

        private static void WriteRoundedRect(string assetPath, int size, float radius)
        {
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            Vector2 center = new(size * 0.5f, size * 0.5f);
            Vector2 half = new(size * 0.5f - radius - 1f, size * 0.5f - radius - 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new(Mathf.Abs(x + 0.5f - center.x), Mathf.Abs(y + 0.5f - center.y));
                    Vector2 q = new(Mathf.Max(p.x - half.x, 0f), Mathf.Max(p.y - half.y, 0f));
                    float distance = q.magnitude - radius;
                    byte alpha = distance <= -0.5f ? (byte)255 : distance >= 0.5f ? (byte)0 : (byte)Mathf.RoundToInt((0.5f - distance) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(ToAbsolutePath(assetPath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static void WriteCircle(string assetPath, int size)
        {
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            Vector2 center = new(size * 0.5f, size * 0.5f);
            float radius = size * 0.5f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) - radius;
                    byte alpha = distance <= -0.5f ? (byte)255 : distance >= 0.5f ? (byte)0 : (byte)Mathf.RoundToInt((0.5f - distance) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(ToAbsolutePath(assetPath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureSpriteImporter(string assetPath, Vector4 border)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
        private static void WriteDiagonalHatch(string assetPath, int size)
        {
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            const float spacing = 12f;

            for (int y = 0; y < size; y++)
            {
                float normalizedY = y / (float)size;
                float wobble =
                    Mathf.Sin(normalizedY * Mathf.PI * 2f) * 0.85f
                    + Mathf.Sin(normalizedY * Mathf.PI * 4f + 1.1f) * 0.4f;

                for (int x = 0; x < size; x++)
                {
                    float wrapped = Mathf.Repeat(x - y + wobble, spacing);
                    float distance = Mathf.Min(wrapped, spacing - wrapped);
                    float coverage = Mathf.Clamp01(1.55f - distance);
                    byte alpha = (byte)Mathf.RoundToInt(coverage * 235f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(ToAbsolutePath(assetPath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
