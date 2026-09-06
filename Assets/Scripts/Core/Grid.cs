using System.Runtime.CompilerServices;

namespace BlastGame.Core
{
    /// <summary>
    /// Index arithmetic for a row-major grid. Pure functions, no state.
    /// </summary>
    /// <remarks>
    /// This lives apart from <see cref="Board"/> because two types need the same arithmetic and only
    /// one of them owns a board: <c>GroupFinder</c> deliberately holds no reference to <see cref="Board"/>
    /// (see DECISIONS.md, Karar 13). Duplicating the formula in both would put the same off-by-one in
    /// two places - and this particular off-by-one is the one flagged in Karar 15, so it is the last
    /// arithmetic in the project that should exist in two copies.
    /// <para>
    /// <b>Row 0 is the bottom row.</b> Unity's world Y grows upward, so <c>worldY = origin.y + row * cellSize</c>
    /// needs no negation anywhere. Gravity therefore falls toward decreasing index; new blocks enter
    /// at the highest index.
    /// </para>
    /// </remarks>
    public static class Grid
    {
        /// <summary>Orthogonal only - diagonals are not adjacency, per the case document.</summary>
        public const int DirectionCount = 4;

        // Direction indices into the step tables below. Named because one caller depends on which is
        // which: the deadlock scan wants every unordered neighbour pair exactly once, so it takes Up and
        // Right and lets the other cell of each pair supply the other half. Everywhere else the order is
        // irrelevant - flood fill visits the whole component regardless, and box damage is stamped once
        // per box per blast.
        public const int Up = 0;
        public const int Down = 1;
        public const int Right = 2;
        public const int Left = 3;

        private static readonly int[] RowStep = { 1, -1, 0, 0 };
        private static readonly int[] ColStep = { 0, 0, 1, -1 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Index(int row, int col, int cols) => row * cols + col;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RowOf(int index, int cols) => index / cols;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ColOf(int index, int cols) => index - (index / cols) * cols;

        /// <summary>
        /// Neighbour of <paramref name="index"/> in the given direction, or false if it falls off the board.
        /// </summary>
        /// <remarks>
        /// Bounds are checked on (row, col) and only then folded back into an index. Checking
        /// <c>index +- 1</c> instead would silently connect the last cell of a row to the first cell
        /// of the next one - they are adjacent in memory but not on the board.
        /// </remarks>
        public static bool TryStep(int index, int direction, int rows, int cols, out int neighbor)
        {
            int row = index / cols;
            int col = index - row * cols;

            int nextRow = row + RowStep[direction];
            int nextCol = col + ColStep[direction];

            if (nextRow < 0 || nextRow >= rows || nextCol < 0 || nextCol >= cols)
            {
                neighbor = -1;
                return false;
            }

            neighbor = nextRow * cols + nextCol;
            return true;
        }
    }
}