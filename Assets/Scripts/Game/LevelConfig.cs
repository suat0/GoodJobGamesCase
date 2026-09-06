using UnityEngine;

namespace BlastGame.Game
{
    /// <summary>
    /// Authoring surface for a level. Core is engine-free, so it never sees this type —
    /// GameController reads the values off and hands Core plain ints.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelConfig", menuName = "Blast/Level Config")]
    public class LevelConfig : ScriptableObject
    {
        // The case document constrains the board to 2-10 on both axes, but its own Example 1
        // uses N=12. The page-1 constraint is the normative one, so the inspector clamps to it;
        // Core imposes no size limit of its own, so a board outside this range still runs.
        public const int MinSize = 2;
        public const int MaxSize = 10;
        public const int MinColors = 1;
        public const int MaxColors = 6;

        [Header("Board")]
        [SerializeField, Range(MinSize, MaxSize)] private int rows = 10;
        [SerializeField, Range(MinSize, MaxSize)] private int cols = 10;
        [SerializeField, Range(MinColors, MaxColors)] private int colorCount = 6;

        [Header("Icon tiers")]
        // A group larger than a threshold shows that tier's icon. Tiers are tested C, B, A so the
        // highest match wins, which means the thresholds must stay strictly ascending: with
        // A=7, B=4 a group of 8 would match ">B" first and the first icon would be unreachable.
        [SerializeField] private int thresholdA = 4;
        [SerializeField] private int thresholdB = 7;
        [SerializeField] private int thresholdC = 9;

        [Header("Objective")]
        // Box count is a request, not a guarantee: Core drops it silently if the board cannot
        // hold that many under the "no Box on the top row" generation rule.
        [SerializeField] private int boxCount = 8;
        [SerializeField] private int moveLimit = 20;
        [SerializeField] private int seed = 0;

        // Read-only to the rest of the game. A ScriptableObject is a single shared asset, and a
        // runtime write to one persists in the editor — the next play session would silently
        // start from mutated values.
        public int Rows => rows;
        public int Cols => cols;
        public int ColorCount => colorCount;

        public int ThresholdA => thresholdA;
        public int ThresholdB => thresholdB;
        public int ThresholdC => thresholdC;

        public int BoxCount => boxCount;

        /// <summary>Zero means unlimited moves. Not a separate mode — the objective is data.</summary>
        public int MoveLimit => moveLimit;

        /// <summary>Zero means a fresh random board each session; any other value reproduces one.</summary>
        public int Seed => seed;

#if UNITY_EDITOR
        // Range attributes only guard the inspector sliders; presets, scripted edits and the
        // reset button all bypass them. This is the actual enforcement.
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
