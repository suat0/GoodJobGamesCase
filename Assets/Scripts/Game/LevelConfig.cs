using UnityEngine;

namespace BlastGame.Game
{
    // Authoring surface for a level. Core is engine-free and never sees this type - GameController
    // reads the values off and hands Core plain ints.
    [CreateAssetMenu(fileName = "LevelConfig", menuName = "Blast/Level Config")]
    public class LevelConfig : ScriptableObject
    {
        // The case document constrains the board to 2-10 but its own Example 1 uses N=12. The page-1
        // constraint is normative, so the inspector clamps; Core imposes no size limit of its own.
        public const int MinSize = 2;
        public const int MaxSize = 10;
        public const int MinColors = 1;
        public const int MaxColors = 6;

        [Header("Board")]
        [SerializeField, Range(MinSize, MaxSize)] private int rows = 10;
        [SerializeField, Range(MinSize, MaxSize)] private int cols = 10;
        [SerializeField, Range(MinColors, MaxColors)] private int colorCount = 6;

        [Header("Icon tiers")]
        // Tested C, B, A so the highest match wins, which means these must stay strictly ascending.
        [SerializeField] private int thresholdA = 4;
        [SerializeField] private int thresholdB = 7;
        [SerializeField] private int thresholdC = 9;

        [Header("Objective")]
        // A request, not a guarantee: Core clamps it if the board cannot hold that many under the
        // "no Box on the top row" rule.
        [SerializeField] private int boxCount = 8;
        [SerializeField] private int moveLimit = 20;
        [SerializeField] private int seed = 0;

        // Read-only: a ScriptableObject is one shared asset, and a runtime write persists in the editor.
        public int Rows => rows;
        public int Cols => cols;
        public int ColorCount => colorCount;

        public int ThresholdA => thresholdA;
        public int ThresholdB => thresholdB;
        public int ThresholdC => thresholdC;

        public int BoxCount => boxCount;

        public int MoveLimit => moveLimit;   // zero means unlimited

        public int Seed => seed;             // zero means a fresh random board each session

#if UNITY_EDITOR
        // Range only guards the sliders; presets, scripted edits and Reset bypass it. This enforces.
        private void OnValidate()
        {
            rows = Mathf.Clamp(rows, MinSize, MaxSize);
            cols = Mathf.Clamp(cols, MinSize, MaxSize);
            colorCount = Mathf.Clamp(colorCount, MinColors, MaxColors);

            thresholdA = Mathf.Max(thresholdA, 1);
            thresholdB = Mathf.Max(thresholdB, thresholdA + 1);
            thresholdC = Mathf.Max(thresholdC, thresholdB + 1);

            boxCount = Mathf.Max(boxCount, 0);
            moveLimit = Mathf.Max(moveLimit, 0);
        }
#endif
    }
}
