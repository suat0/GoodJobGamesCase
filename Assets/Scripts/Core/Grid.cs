using System.Runtime.CompilerServices;

namespace BlastGame.Core
{
    // Index arithmetic for a row-major grid. Row 0 is the BOTTOM row, so worldY = origin.y + row * cellSize
    // needs no negation and gravity falls toward decreasing index.
    public static class Grid
    {
        public const int DirectionCount = 4;   // orthogonal only

        // The deadlock scan depends on these two being distinct: it walks Up and Right so every
        // unordered neighbour pair is offered exactly once. Elsewhere the order is irrelevant.
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

        public static bool TryStep(int index, int direction, int rows, int cols, out int neighbor)
        {
            int row = index / cols;
            int col = index - row * cols;

            int nextRow = row + RowStep[direction];
            int nextCol = col + ColStep[direction];

            // Bounds are checked on (row, col), never on index +- 1: that would connect the last cell
            // of a row to the first cell of the next one.
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
