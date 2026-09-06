using System;

namespace BlastGame.Core
{
    /// <summary>
    /// Everything Core needs to build and run a board. A plain value, not an abstraction: it exists so
    /// that five consecutive <c>int</c> parameters do not have to be passed positionally.
    /// </summary>
    /// <remarks>
    /// The Unity-side <c>LevelConfig</c> is a ScriptableObject and cannot cross into Core, so it copies
    /// its values into one of these. That copy is the single conversion point between the two worlds -
    /// and the only place a future field has to be threaded through.
    /// <para>
    /// Move limit and seed are deliberately absent. Neither shapes a board: the move limit belongs to
    /// the game loop, and the seed is used to construct the <see cref="Random"/> that gets injected.
    /// </para>
    /// </remarks>
    public readonly struct BoardConfig
    {
        public readonly int Rows;
        public readonly int Cols;
        public readonly int ColorCount;

        /// <summary>Group size above which a block shows the first icon. Strictly greater, per the case rules.</summary>
        public readonly int ThresholdA;
        public readonly int ThresholdB;
        public readonly int ThresholdC;

        /// <summary>Requested Boxes. Clamped during generation to what the top-row rule allows.</summary>
        public readonly int BoxCount;

        public BoardConfig(int rows, int cols, int colorCount,
                           int thresholdA, int thresholdB, int thresholdC,
                           int boxCount)
        {
            Rows = rows;
            Cols = cols;
            ColorCount = colorCount;
            ThresholdA = thresholdA;
            ThresholdB = thresholdB;
            ThresholdC = thresholdC;
            BoxCount = boxCount;
        }

        /// <summary>
        /// Throws if the values cannot describe a board. Lives here rather than only in LevelConfig's
        /// OnValidate because tests build configs directly and never touch the inspector.
        /// </summary>
        public void Validate()
        {
            // The case document's 2-10 range is an authoring constraint enforced in LevelConfig. Core
            // rejects only what it cannot represent, which is also why the document's own N=12 example
            // still runs.
            if (Rows < 1) throw new ArgumentOutOfRangeException(nameof(Rows), Rows, "Board needs at least one row.");
            if (Cols < 1) throw new ArgumentOutOfRangeException(nameof(Cols), Cols, "Board needs at least one column.");

            // Colours are stored in a byte.
            if (ColorCount < 1 || ColorCount > 256)
                throw new ArgumentOutOfRangeException(nameof(ColorCount), ColorCount, "Colour count must be in [1, 256].");

            if (BoxCount < 0)
                throw new ArgumentOutOfRangeException(nameof(BoxCount), BoxCount, "Box count cannot be negative.");

            // Tiers are tested C, B, A so the highest match wins. Equal or descending thresholds would
            // make an icon unreachable rather than fail loudly: with A=7, B=4 a group of 8 matches ">B"
            // first and the first icon is never shown. Strictly ascending is what makes the order correct.
            if (ThresholdA < 1)
                throw new ArgumentOutOfRangeException(nameof(ThresholdA), ThresholdA, "Thresholds must be positive.");
            if (ThresholdB <= ThresholdA || ThresholdC <= ThresholdB)
                throw new ArgumentException(
                    $"Thresholds must strictly ascend, got A={ThresholdA}, B={ThresholdB}, C={ThresholdC}.");
        }
    }
}