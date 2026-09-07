#if UNITY_EDITOR
using System.IO;
using BlastGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BlastGame.Game.EditorTools
{
    // Renders the scene to a PNG without entering play mode, so a layout change can be looked at
    // rather than described. Edits the open scene and never saves it.
    public static class HudPreview
    {
        private const string ScenePath = "Assets/Scenes/Game.unity";

        private const int Width = 1080;
        private const int Height = 1920;

        public static void Capture()
        {
            string output = System.Environment.GetEnvironmentVariable("PREVIEW_OUT");
            if (string.IsNullOrEmpty(output)) output = "preview.png";

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Camera camera = Camera.main;
            var boardView = Object.FindFirstObjectByType<BoardView>();
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var hud = Object.FindFirstObjectByType<BlastGame.Game.UI.HudView>();

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            camera.targetTexture = rt;
            camera.aspect = (float)Width / Height;

            // An overlay canvas is composited by the screen, not by a camera, so it never lands in a
            // render texture. Only for the capture.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;

            // Drawn here rather than through BoardView: the pooled blocks cache their transform in
            // Awake, which edit mode never calls. The preview only has to show the HUD over a
            // plausible board, so it lays sprites out itself and leaves the runtime path alone.
            var config = new BoardConfig(10, 10, 6, 4, 7, 9, 8);
            var board = new Board(config, new System.Random(12345));
            board.Generate();

            DrawBoard(board, camera, boardView.transform);

            FillLabels(hud);

            Canvas.ForceUpdateCanvases();
            camera.Render();

            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = null;

            File.WriteAllBytes(output, image.EncodeToPNG());

            camera.targetTexture = null;
            Debug.Log($"Preview written to {output}.");
        }

        private static readonly string[] ColorNames = { "Blue", "Green", "Pink", "Purple", "Red", "Yellow" };

        private static void DrawBoard(Board board, Camera camera, Transform parent)
        {
            var origin = new Vector3(-(board.Cols - 1) * 0.5f, -(board.Rows - 1) * 0.5f, 0f);

            for (int i = 0; i < board.CellCount; i++)
            {
                Cell cell = board.CellAt(i);

                string sprite = cell.IsBox ? "Box0" : cell.IsColor ? ColorNames[cell.Color] + "_Default" : null;
                if (sprite == null) continue;

                var go = new GameObject(sprite);
                go.transform.SetParent(parent, false);

                int row = i / board.Cols;
                go.transform.localPosition = origin + new Vector3(i - row * board.Cols, row, 0f);

                go.AddComponent<SpriteRenderer>().sprite =
                    AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/{sprite}.png");
            }

            // Sized the way FitFrame does at runtime, since Bind is not being called here.
            Transform frame = parent.Find("BoardFrame");
            if (frame != null)
                frame.GetComponent<SpriteRenderer>().size = new Vector2(board.Cols + 0.6f, board.Rows + 0.6f);

            // The same fit BoardView performs, so the framing in the picture is the real framing.
            float vertical = board.Rows * 0.5f + 0.5f;
            float horizontal = (board.Cols * 0.5f + 0.5f) / camera.aspect;
            camera.orthographicSize = Mathf.Max(vertical, horizontal);
        }

        // HudView reads a live GameSession, which does not exist outside play mode, so the labels are
        // filled with representative values instead.
        private static void FillLabels(BlastGame.Game.UI.HudView hud)
        {
            var so = new SerializedObject(hud);
            SetText(so, "scoreLabel", "12 480");
            SetText(so, "movesLabel", "23");
            SetText(so, "objectiveLabel", "8");

            var overPanel = (GameObject)so.FindProperty("gameOverPanel").objectReferenceValue;
            bool showPopup = System.Environment.GetEnvironmentVariable("PREVIEW_POPUP") == "1";
            overPanel.SetActive(showPopup);

            if (!showPopup) return;

            var dim = (CanvasGroup)so.FindProperty("gameOverDim").objectReferenceValue;
            dim.alpha = 1f;
        }

        private static void SetText(SerializedObject so, string field, string value)
        {
            var label = (TMPro.TMP_Text)so.FindProperty(field).objectReferenceValue;
            label.text = value;
        }
    }
}
#endif
