#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BlastGame.Game.EditorTools
{
    // Rebuilds the HUD from scratch and saves the scene. The layout lives here rather than in the
    // scene file alone so it can be read, reviewed and re-run; a canvas assembled by dragging is a
    // diff nobody can review.
    public static class HudBuilder
    {
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string FontPath = "Assets/Fonts/Baloo2-ExtraBold SDF.asset";
        private const string BoxSpritePath = "Assets/Art/Box0.png";

        // One place for every colour in the HUD. Hex rather than Color32 literals because these were
        // picked against the block art, and a hex string is what a designer would hand over.
        private static readonly Color Ink = Hex("#2E2153");
        private static readonly Color BarFill = Hex("#44338C");
        private static readonly Color BarShade = Hex("#241A4A");
        private static readonly Color CaptionInk = Hex("#B9AEE8");
        private static readonly Color ValueInk = Hex("#FFFFFF");
        private static readonly Color ScoreValue = Hex("#FFD84D");
        private static readonly Color Dim = new Color(0.04f, 0.02f, 0.11f, 0.82f);
        private static readonly Color CardFill = Hex("#FFF3DC");
        private static readonly Color CardShade = Hex("#C7A87F");
        private static readonly Color ButtonFill = Hex("#F2A03D");
        private static readonly Color ButtonShade = Hex("#C2762A");
        private static readonly Color Sky = Hex("#1E1638");
        private static readonly Color Well = Hex("#17102F");

        private static TMP_FontAsset font;
        private static Sprite panelSprite;

        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) throw new System.IO.FileNotFoundException(FontPath);

            // Unity's own rounded nine-slice, the one a fresh Image starts with. No download, no
            // licence question, and tinted per panel it carries the whole HUD.
            panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            var hud = Object.FindFirstObjectByType<BlastGame.Game.UI.HudView>();
            if (hud == null) throw new MissingComponentException("No HudView in the scene.");

            Transform canvas = hud.transform;
            for (int i = canvas.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(canvas.GetChild(i).gameObject);

            BuildTopBar(canvas, out TMP_Text score, out TMP_Text moves, out TMP_Text objective, out GameObject objectiveGroup);
            BuildPopup(canvas, out GameObject panel, out CanvasGroup dim, out RectTransform card, out TMP_Text message, out Button restart);

            var so = new SerializedObject(hud);
            so.FindProperty("scoreLabel").objectReferenceValue = score;
            so.FindProperty("movesLabel").objectReferenceValue = moves;
            so.FindProperty("objectiveLabel").objectReferenceValue = objective;
            so.FindProperty("objectiveGroup").objectReferenceValue = objectiveGroup;
            so.FindProperty("gameOverPanel").objectReferenceValue = panel;
            so.FindProperty("gameOverDim").objectReferenceValue = dim;
            so.FindProperty("gameOverCard").objectReferenceValue = card;
            so.FindProperty("gameOverLabel").objectReferenceValue = message;
            so.FindProperty("restartButton").objectReferenceValue = restart;
            so.ApplyModifiedPropertiesWithoutUndo();

            BuildBackdrop();

            Camera camera = Camera.main;
            if (camera != null) camera.backgroundColor = Sky;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("HUD rebuilt.");
        }

        // The gradient behind everything and the well the grid sits in. Both use sprites from
        // BlockAtlas, so they share the blocks' material and cost no extra draw call.
        private static void BuildBackdrop()
        {
            var boardView = Object.FindFirstObjectByType<BoardView>();
            Transform game = boardView.transform;

            Replace(game, "Backdrop");
            Replace(game, "BoardFrame");

            var backdrop = new GameObject("Backdrop", typeof(SpriteRenderer));
            backdrop.transform.SetParent(game, false);

            // Wide and tall enough for the widest framing any allowed board can ask for; the sprite
            // is a plain vertical strip, so overshooting costs nothing.
            backdrop.transform.localScale = new Vector3(20f, 1f, 1f);

            var backdropRenderer = backdrop.GetComponent<SpriteRenderer>();
            backdropRenderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Backdrop.png");
            backdropRenderer.sortingOrder = -20;

            var frame = new GameObject("BoardFrame", typeof(SpriteRenderer));
            frame.transform.SetParent(game, false);

            var frameRenderer = frame.GetComponent<SpriteRenderer>();
            frameRenderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Frame.png");
            frameRenderer.drawMode = SpriteDrawMode.Sliced;      // corners keep their radius at any size
            frameRenderer.color = Well;
            frameRenderer.sortingOrder = -10;

            var so = new SerializedObject(boardView);
            so.FindProperty("boardFrame").objectReferenceValue = frameRenderer;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Replace(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
        }

        private static void BuildTopBar(Transform canvas, out TMP_Text score, out TMP_Text moves,
                                        out TMP_Text objective, out GameObject objectiveGroup)
        {
            RectTransform bar = Rect("TopBar", canvas);
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(-80f, 180f);      // 40px of margin on each side
            bar.anchoredPosition = new Vector2(0f, -70f);

            // Drawn first so the fill covers it except where it is offset: a solid shadow, not a
            // blurred one, which is what the block art does too.
            Panel("Shadow", bar, BarShade, 0f, -12f);
            Panel("Fill", bar, BarFill, 0f, 0f);

            RectTransform movesColumn = Column("Moves", bar, 0f, 0.34f);
            Caption("Caption", movesColumn, "MOVES");
            moves = Value("Value", movesColumn, "0", ValueInk);

            RectTransform scoreColumn = Column("Score", bar, 0.34f, 0.66f);
            Caption("Caption", scoreColumn, "SCORE");
            score = Value("Value", scoreColumn, "0", ScoreValue);

            // No caption: the icon says what is being counted, in every language and to a player who
            // cannot tell the block colours apart.
            RectTransform objectiveColumn = Column("Objective", bar, 0.66f, 1f);
            objectiveGroup = objectiveColumn.gameObject;

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)icon.transform;
            iconRect.SetParent(objectiveColumn, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(100f, 100f);
            iconRect.anchoredPosition = new Vector2(-72f, 0f);

            var iconImage = icon.GetComponent<Image>();
            iconImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BoxSpritePath);
            MakeNonInteractive(iconImage);

            objective = Text("Count", objectiveColumn, "0", 68f, ValueInk, TextAlignmentOptions.Left);
            RectTransform countRect = objective.rectTransform;
            countRect.anchorMin = countRect.anchorMax = new Vector2(0.5f, 0.5f);
            countRect.sizeDelta = new Vector2(150f, 100f);
            countRect.anchoredPosition = new Vector2(70f, -6f);
        }

        private static void BuildPopup(Transform canvas, out GameObject panel, out CanvasGroup dim,
                                       out RectTransform card, out TMP_Text message, out Button restart)
        {
            RectTransform root = Rect("GameOver", canvas);
            Stretch(root, 0f, 0f);
            panel = root.gameObject;

            RectTransform dimRect = Panel("Dim", root, Dim, 0f, 0f);
            dimRect.GetComponent<Image>().sprite = null;      // a flat wash, not a rounded panel

            // Blocks taps on the board behind it. The dim is the only raycast target in the popup
            // that has to be one.
            Image dimImage = dimRect.GetComponent<Image>();
            dimImage.raycastTarget = true;
            dimImage.maskable = false;
            dim = dimRect.gameObject.AddComponent<CanvasGroup>();

            card = Rect("Card", root);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(820f, 470f);

            // Both inside the card, so the entrance scales the whole thing as one object.
            Panel("Shadow", card, CardShade, 0f, -16f);
            Panel("Fill", card, CardFill, 0f, 0f);

            message = Text("Message", card, "Level complete", 84f, Ink, TextAlignmentOptions.Center);
            RectTransform messageRect = message.rectTransform;
            messageRect.anchorMin = new Vector2(0f, 1f);
            messageRect.anchorMax = new Vector2(1f, 1f);
            messageRect.pivot = new Vector2(0.5f, 1f);
            messageRect.sizeDelta = new Vector2(-100f, 170f);
            messageRect.anchoredPosition = new Vector2(0f, -62f);

            RectTransform area = Rect("RestartArea", card);
            area.anchorMin = area.anchorMax = new Vector2(0.5f, 0f);
            area.sizeDelta = new Vector2(480f, 160f);
            area.anchoredPosition = new Vector2(0f, 96f);

            Panel("Shadow", area, ButtonShade, 0f, -14f);

            RectTransform buttonRect = Panel("RestartButton", area, ButtonFill, 0f, 0f);
            Image buttonImage = buttonRect.GetComponent<Image>();
            buttonImage.raycastTarget = true;
            buttonImage.maskable = false;

            restart = buttonRect.gameObject.AddComponent<Button>();
            restart.targetGraphic = buttonImage;

            TMP_Text label = Text("Label", buttonRect, "Play again", 56f, ValueInk, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 0f, 0f);
        }

        // --- small builders -------------------------------------------------------------------

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform Panel(string name, Transform parent, Color color, float offsetX, float offsetY)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect, offsetX, offsetY);

            var image = go.GetComponent<Image>();
            image.sprite = panelSprite;
            image.type = Image.Type.Sliced;
            image.color = color;

            MakeNonInteractive(image);

            // Below one, the nine-slice borders scale up: Unity's sprite has a small corner made for
            // a small button, and these panels are hundreds of pixels wide.
            image.pixelsPerUnitMultiplier = 0.25f;

            return rect;
        }

        private static RectTransform Column(string name, Transform parent, float min, float max)
        {
            RectTransform column = Rect(name, parent);
            column.anchorMin = new Vector2(min, 0f);
            column.anchorMax = new Vector2(max, 1f);
            column.offsetMin = Vector2.zero;
            column.offsetMax = Vector2.zero;
            return column;
        }

        private static void Caption(string name, Transform parent, string content)
        {
            TMP_Text caption = Text(name, parent, content, 30f, CaptionInk, TextAlignmentOptions.Center);
            RectTransform rect = caption.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, 44f);
            rect.anchoredPosition = new Vector2(0f, -22f);

            caption.characterSpacing = 6f;
        }

        private static TMP_Text Value(string name, Transform parent, string content, Color color)
        {
            TMP_Text value = Text(name, parent, content, 78f, color, TextAlignmentOptions.Center);
            RectTransform rect = value.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0f, 104f);
            rect.anchoredPosition = new Vector2(0f, 14f);
            return value;
        }

        private static TMP_Text Text(string name, Transform parent, string content, float size,
                                     Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.text = content;

            MakeNonInteractive(text);

            return text;
        }

        // Two opt-outs, both for graphics nobody interacts with. raycastTarget keeps the graphic off
        // the list the raycaster walks on every tap; maskable keeps it out of the stencil test uGUI
        // otherwise runs in case a Mask is above it. Neither panel nor label is ever masked or tapped,
        // and the two flags are the ones a HUD leaves on by accident.
        // Safe only because this canvas has no Mask or RectMask2D - under one, maskable false would
        // make the graphic ignore the mask entirely rather than be clipped by it.
        private static void MakeNonInteractive(MaskableGraphic graphic)
        {
            if (graphic == null) return;

            graphic.raycastTarget = false;
            graphic.maskable = false;
        }

        private static void Stretch(RectTransform rect, float offsetX, float offsetY)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(offsetX, offsetY);
            rect.offsetMax = new Vector2(offsetX, offsetY);
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color color);
            return color;
        }
    }
}
#endif
