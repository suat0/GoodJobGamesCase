using System;

namespace BlastGame.Core
{
    // Everything that changed in one move, in the form the view needs to animate it.
    // One instance, refilled every move: listeners read it during the call and never store it.
    public sealed class BlastResult
    {
        private readonly int cellCount;
        private readonly int cols;
        private readonly int rows;

        private readonly int[] removed;
        private int removedCount;

        private readonly int[] moveFrom;
        private readonly int[] moveTo;
        private int moveCount;

        private readonly int[] damagedBoxes;
        private int damagedCount;

        private readonly int[] brokenBoxes;
        private int brokenCount;

        public int TappedIndex { get; internal set; }

        public int BlastedGroupSize { get; internal set; }

        public BlastResult(BoardConfig config)
        {
            rows = config.Rows;
            cols = config.Cols;
            cellCount = rows * cols;

            removed = new int[cellCount];

            // A move is identified by where it lands and no two blocks land on the same cell, so one
            // turn cannot produce more moves than there are cells - spawns included.
            moveFrom = new int[cellCount];
            moveTo = new int[cellCount];

            damagedBoxes = new int[cellCount];
            brokenBoxes = new int[cellCount];
        }

        public ReadOnlySpan<int> Removed => new ReadOnlySpan<int>(removed, 0, removedCount);

        public ReadOnlySpan<int> MoveFrom => new ReadOnlySpan<int>(moveFrom, 0, moveCount);

        public ReadOnlySpan<int> MoveTo => new ReadOnlySpan<int>(moveTo, 0, moveCount);

        // Boxes that took a hit and survived; the view swaps their sprite.
        public ReadOnlySpan<int> DamagedBoxes => new ReadOnlySpan<int>(damagedBoxes, 0, damagedCount);

        // Boxes destroyed this move; the view plays a particle here.
        public ReadOnlySpan<int> BrokenBoxes => new ReadOnlySpan<int>(brokenBoxes, 0, brokenCount);

        // New blocks are ordinary moves whose source sits above the top row. Decoding that index with
        // the usual arithmetic gives the spawn position the view needs, so the view has one rule
        // ("move each block from its source to its target") instead of two.
        public bool IsSpawn(int fromIndex) => fromIndex >= cellCount;

        public int RowOfSource(int fromIndex) => fromIndex / cols;

        public int ColOfSource(int fromIndex) => fromIndex - (fromIndex / cols) * cols;

        internal void Clear()
        {
            // Counts only. Stale values below them are unreachable through the spans.
            removedCount = 0;
            moveCount = 0;
            damagedCount = 0;
            brokenCount = 0;

            TappedIndex = -1;
            BlastedGroupSize = 0;
        }

        internal void AddRemoved(int index) => removed[removedCount++] = index;

        internal void AddMove(int from, int to)
        {
            moveFrom[moveCount] = from;
            moveTo[moveCount] = to;
            moveCount++;
        }

        // Stacked above the top row in landing order, so blocks in one column keep their spacing.
        internal int SpawnSource(int column, int order) => (rows + order) * cols + column;

        internal void AddDamagedBox(int index) => damagedBoxes[damagedCount++] = index;

        internal void AddBrokenBox(int index) => brokenBoxes[brokenCount++] = index;
    }
}
