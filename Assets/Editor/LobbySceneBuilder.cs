using SuperQQ.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SuperQQ.EditorTools
{
    /// <summary>一键搭建 Lobby 场景：UI + NetworkManager + LobbyController</summary>
    public static class LobbySceneBuilder
    {
        [MenuItem("SuperQQ/Build Lobby Scene")]
        public static void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.12f, 0.14f, 0.2f);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            MakeText(font, "Title", "SuperQQ 联机大厅", 72, new Vector2(0, 250), new Vector2(800, 100), canvasGo.transform, Color.white, TextAnchor.MiddleCenter);

            // 输入框
            var inputGo = new GameObject("RoomNameInput");
            inputGo.transform.SetParent(canvasGo.transform, false);
            var inputImg = inputGo.AddComponent<Image>();
            inputImg.color = Color.white;
            var inputRt = inputGo.GetComponent<RectTransform>();
            inputRt.anchoredPosition = new Vector2(0, 80);
            inputRt.sizeDelta = new Vector2(500, 70);
            var input = inputGo.AddComponent<InputField>();

            var ph = MakeText(font, "Placeholder", "输入房间名...", 36, Vector2.zero, Vector2.zero, inputGo.transform, new Color(0.5f, 0.5f, 0.5f), TextAnchor.MiddleLeft);
            Stretch(ph.GetComponent<RectTransform>(), 20);
            var txt = MakeText(font, "Text", "", 36, Vector2.zero, Vector2.zero, inputGo.transform, Color.black, TextAnchor.MiddleLeft);
            txt.supportRichText = false;
            Stretch(txt.GetComponent<RectTransform>(), 20);
            input.placeholder = ph;
            input.textComponent = txt;

            // 按钮
            var btnGo = new GameObject("JoinButton");
            btnGo.transform.SetParent(canvasGo.transform, false);
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.2f, 0.6f, 0.3f);
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchoredPosition = new Vector2(0, -40);
            btnRt.sizeDelta = new Vector2(300, 80);
            var btn = btnGo.AddComponent<Button>();
            MakeText(font, "Text", "加入房间", 40, Vector2.zero, new Vector2(300, 80), btnGo.transform, Color.white, TextAnchor.MiddleCenter);

            var status = MakeText(font, "StatusText", "", 30, new Vector2(0, -180), new Vector2(900, 60), canvasGo.transform, new Color(1f, 0.9f, 0.4f), TextAnchor.MiddleCenter);

            // NetworkManager
            var netGo = new GameObject("NetworkManager");
            netGo.AddComponent<NetworkManager>();

            // LobbyController
            var lobbyGo = new GameObject("LobbyController");
            var lobby = lobbyGo.AddComponent<LobbyController>();
            var so = new SerializedObject(lobby);
            so.FindProperty("roomNameInput").objectReferenceValue = input;
            so.FindProperty("joinButton").objectReferenceValue = btn;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            EditorSceneManager.SaveScene(canvasGo.scene);
            Debug.Log("[LobbySceneBuilder] Lobby 场景搭建完成并已保存");
        }

        private static Text MakeText(Font font, string name, string content, int size, Vector2 pos, Vector2 sizeV, Transform parent, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeV;
            return t;
        }

        private static void Stretch(RectTransform rt, float paddingLeft)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(paddingLeft, 0);
            rt.offsetMax = Vector2.zero;
        }
    }
}
