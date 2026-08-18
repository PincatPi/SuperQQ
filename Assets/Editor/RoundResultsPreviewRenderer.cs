using System.Collections.Generic;
using System.IO;
using SuperQQ.Score;
using SuperQQ.UI.RoundResults;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SuperQQ.EditorTools
{
    public static class RoundResultsPreviewRenderer
    {
        private const string PanelPrefabPath = "Assets/Prefab/UI/RoundResults/RoundResultsPanel.prefab";
        private const string PreviewPath = "Assets/Art/UI/RoundResults/round_results_preview.png";

[MenuItem("Tools/SuperQQ/UI/Render Round Results Preview")]
        public static void RenderPreview()
        {
            GameObject panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            if (panelPrefab == null)
            {
                Debug.LogError($"[RoundResultsPreviewRenderer] Missing prefab: {PanelPrefabPath}");
                return;
            }

            UnityEngine.SceneManagement.Scene previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            RenderTexture renderTexture = new(1280, 720, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1
            };
            Texture2D screenshot = null;
            Camera camera = null;

            try
            {
                camera = CreateCamera(previewScene);
                camera.targetTexture = renderTexture;
                Canvas canvas = CreateCanvas(previewScene, camera);

                GameObject panelInstance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, previewScene);
                panelInstance.transform.SetParent(canvas.transform, false);
                RectTransform panelRect = panelInstance.GetComponent<RectTransform>();
                panelRect.anchorMin = Vector2.zero;
                panelRect.anchorMax = Vector2.one;
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;
                panelRect.localScale = Vector3.one;
                panelRect.anchoredPosition = Vector2.zero;
                panelInstance.SetActive(true);

                RoundResultsPanel panel = panelInstance.GetComponent<RoundResultsPanel>();
                panel.ShowImmediate(CreateSampleData(), 3, 100);
                SetLayerRecursively(canvas.gameObject, 31);

                Canvas.ForceUpdateCanvases();
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                screenshot = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
                screenshot.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
                screenshot.Apply();
                RenderTexture.active = previous;

                string absolutePath = Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    PreviewPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
                File.WriteAllBytes(absolutePath, screenshot.EncodeToPNG());
                AssetDatabase.ImportAsset(PreviewPath, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[RoundResultsPreviewRenderer] Rendered {PreviewPath}");
            }
            finally
            {
                if (camera != null)
                {
                    camera.targetTexture = null;
                }

                if (screenshot != null)
                {
                    Object.DestroyImmediate(screenshot);
                }

                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
                EditorSceneManager.CloseScene(previewScene, true);
            }
        }

private static Camera CreateCamera(UnityEngine.SceneManagement.Scene scene)
        {
            GameObject cameraObject = new("PreviewCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.layer = 31;
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(24, 34, 59, 255);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.cullingMask = 1 << 31;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            return camera;
        }

private static Canvas CreateCanvas(UnityEngine.SceneManagement.Scene scene, Camera camera)
        {
            GameObject canvasObject = new(
                "PreviewCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, scene);

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1280f, 720f);
            canvasRect.position = Vector3.zero;
            canvasRect.localScale = Vector3.one * 0.01f;

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            return canvas;
        }

private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
            }
        }


        private static List<RoundResultPlayerData> CreateSampleData()
        {
            return new List<RoundResultPlayerData>
            {
                CreatePlayer("MINT", new Color32(56, 188, 174, 255), 43, true,
                    Segment(ScoreType.Completion, 20),
                    Segment(ScoreType.FirstPlace, 10),
                    Segment(ScoreType.ScoreItem, 6),
                    Segment(ScoreType.TrapKill, 7)),
                CreatePlayer("TURTLE", new Color32(238, 143, 68, 255), 34, false,
                    Segment(ScoreType.Completion, 20),
                    Segment(ScoreType.SpecialEffect, 10)),
                CreatePlayer("BERRY", new Color32(218, 91, 125, 255), 25, false,
                    Segment(ScoreType.Completion, 20),
                    Segment(ScoreType.ScoreItem, 5)),
                CreatePlayer("MOMO", new Color32(132, 103, 211, 255), 9, false,
                    Segment(ScoreType.TrapKill, 10))
            };
        }

        private static RoundResultPlayerData CreatePlayer(
            string name,
            Color color,
            int previous,
            bool winner,
            params RoundResultScoreSegment[] segments)
        {
            RoundResultPlayerData player = new()
            {
                PlayerName = name,
                PlayerColor = color,
                PreviousTotal = previous,
                IsRoundWinner = winner
            };

            int roundTotal = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                player.Segments.Add(segments[i]);
                roundTotal += segments[i].Points;
            }

            player.RoundTotal = roundTotal;
            player.CumulativeTotal = previous + roundTotal;
            return player;
        }

        private static RoundResultScoreSegment Segment(ScoreType type, int points)
        {
            return new RoundResultScoreSegment
            {
                ScoreType = type,
                Label = RoundResultsDataAdapter.GetSegmentLabel(type),
                Points = points,
                Color = RoundResultsDataAdapter.GetSegmentColor(type)
            };
        }
    }
}
