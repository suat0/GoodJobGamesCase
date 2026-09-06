using System;

namespace BlastGame.Core
{
    /// <summary>
    /// Everything that changed in one move, in the form the view needs to animate it.
    /// </summary>
    /// <remarks>
    /// <b>One instance, refilled.</b> The board reuses a single result across the whole session, so a
    /// move costs no allocation. The consequence is a rule listeners must honour: read it during the
    /// notification, never store it. A reference kept past the call shows the next move's data.
    /// <para>
    /// <b>Fixed arrays, not lists.</b> Every list here is bounded by the cell count - no move can remove,
    /// move or break more cells than exist - so the one thing a <c>List</c> offers, growth, is the one
    /// thing that never happens. A list would still allocate while its capacity settled during the first
    /// few moves; an array sized up front is at zero from the first frame.
    /// </para>
    /// <para>
    /// <b>Mutators are internal.</b> Core fills this in; the view only reads. That is enforced by the
    /// assembly boundary rather than by convention.
    /// </para>
    /// </remarks>
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

        /// <summary>Cell the player tapped.</summary>
        public int TappedIndex { get; internal set; }

        /// <summary>How many blocks the tapped group contained. Scoring reads this.</summary>
        public int BlastedGroupSize { get; internal set; }

        public BlastResult(BoardConfig config)
        {
            rows = config.Rows;
            cols = config.Cols;
            cellCount = rows * cols;

            removed = new int[cellCount];

            // A move is identified by where it lands, and no two blocks land on the same cell, so the
            // number of moves in one turn cannot exceed the number of cells - spawns included.
            moveFrom = new int[cellCount];
            moveTo = new int[cellCount];

            damagedBoxes = new int[cellCount];
            brokenBoxes = new int[cellCount];
        }

        /// <summary>Cells whose block was blasted away.</summary>
        public ReadOnlySpan<int> Removed => new ReadOnlySpan<int>(removed, 0, removedCount);

        /// <summary>Where each moving block came from, parallel to <see cref="MoveTo"/>.</summary>
        public ReadOnlySpan<int> MoveFrom => new ReadOnlySpan<int>(moveFrom, 0, moveCount);

        /// <summary>Where each moving block ended up.</summary>
        public ReadOnlySpan<int> MoveTo => new ReadOnlySpan<int>(moveTo, 0, moveCount);

        /// <summary>Boxes that took a hit and survived. The view swaps their sprite.</summary>
        public ReadOnlySpan<int> DamagedBoxes => new ReadOnlySpan<int>(damagedBoxes, 0, damagedCount);

        /// <summary>Boxes destroyed this move. The view plays a particle here.</summary>
        public ReadOnlySpan<int> BrokenBoxes => new ReadOnlySpan<int>(brokenBoxes, 0, brokenCount);

        /// <summary>
        /// Whether a move started outside the board, i.e. the block is newly created.
        /// </summary>
        /// <remarks>
        /// New blocks are recorded as ordinary moves whose source index sits above the top row, rather
        /// than in a list of their own. Decoding that index with the usual row/column arithmetic gives a
        /// row beyond the board and the right column, which is exactly the spawn position the view needs
        /// - so the view has one rule ("move each block from its source to its target") instead of two.
        /// <para>
        /// It also lines up with the settled-block filter: a block born above the board has never been
        /// settled, so taps on it are already rejected without a special case.
        /// </para>
        /// </remarks>
        public bool IsSpawn(int fromIndex) => fromIndex >= cellCount;

        /// <summary>Row of a source index, valid for spawns too - they simply report a row above the board.</summary>
        public int RowOfSource(int fromIndex) => fromIndex / cols;

        /// <summary>Column of a source index, valid for spawns too.</summary>
        public int ColOfSource(int fromIndex) => fromIndex - (fromIndex / cols) * cols;

        internal void Clear()
        {
            // Only the counts are reset. Stale values below them are unreachable through the spans, and
            // zeroing thousands of ints every move to hide data nobody can read would be busywork.
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

        /// <summary>Source index for the <paramref name="order"/>-th new block of a column, counting upward.</summary>
        /// <remarks>
        /// Stacked above the top row in landing order, so blocks in the same column keep their spacing
        /// and fall together instead of overlapping.
        /// </remarks>
        internal int SpawnSource(int column, int order) => (rows + order) * cols + column;

        internal void AddDamagedBox(int index) => damagedBoxes[damagedCount++] = index;

        internal void AddBrokenBox(int index) => brokenBoxes[brokenCount++] = index;
    }
}