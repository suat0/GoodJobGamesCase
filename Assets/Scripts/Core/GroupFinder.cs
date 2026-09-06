using System;
using System.Diagnostics;

namespace BlastGame.Core
{
    /// <summary>
    /// Finds the connected components of same-coloured cells on a board, and caches their sizes.
    /// </summary>
    /// <remarks>
    /// <b>Owns scratch, borrows data.</b> The three working arrays are allocated once here and reused
    /// on every scan; the board itself arrives per call as a <see cref="ReadOnlySpan{T}"/> and is never
    /// retained (DECISIONS.md, Karar 13). Nothing held means nothing can go stale, and the span type
    /// says at compile time that this class only reads the board.
    /// <para>
    /// One full scan answers three questions at once (Karar 3): what is blastable, what icon each block
    /// shows, and whether the board is deadlocked. An incremental version would be faster in principle
    /// and wrong in practice - a single column collapsing can merge or split groups several columns away.
    /// </para>
    /// </remarks>
    public sealed class GroupFinder
    {
        /// <summary>Two adjacent same-coloured cells are enough to blast, per the case document.</summary>
        public const int MinBlastableSize = 2;

        private const int NoGroup = -1;

        private readonly int rows;
        private readonly int cols;

        // Fixed for the lifetime of the board, exactly like rows and cols. Passing them to Recalculate
        // instead would mean storing them anyway, since TierAt is asked after the scan, not during it.
        private readonly int thresholdA;
        private readonly int thresholdB;
        private readonly int thresholdC;

        /// <summary>Group each cell belongs to, or <see cref="NoGroup"/> for Empty and Box cells.</summary>
        private readonly int[] groupIdOf;

        /// <summary>Size of each group, indexed by group id. Only the first <see cref="GroupCount"/> entries are live.</summary>
        private readonly int[] groupSizes;

        /// <summary>
        /// Explicit DFS stack. Recursion would be the obvious way to write flood fill and the wrong one:
        /// a single-colour 10x10 board is a 100-deep call chain, and the depth grows with the board.
        /// A <c>Queue&lt;int&gt;</c> (BFS) would allocate; both traverse the whole component anyway,
        /// since icon tiers need the exact size and there is no early exit.
        /// </summary>
        private readonly int[] stack;

        /// <summary>Groups found by the last scan.</summary>
        public int GroupCount { get; private set; }

        /// <summary>
        /// Size of the biggest group in the last scan. Deadlock detection falls out of this for free -
        /// no group of <see cref="MinBlastableSize"/> means no legal move - so it costs no extra pass.
        /// </summary>
        public int LargestGroupSize { get; private set; }

        public GroupFinder(BoardConfig config)
        {
            config.Validate();

            rows = config.Rows;
            cols = config.Cols;

            thresholdA = config.ThresholdA;
            thresholdB = config.ThresholdB;
            thresholdC = config.ThresholdC;

            int cellCount = rows * cols;

            groupIdOf = new int[cellCount];

            // Worst case is every coloured cell isolated, so there can be as many groups as cells.
            groupSizes = new int[cellCount];

            // A cell is marked the moment it is pushed, never when it is popped, so it can be pushed
            // at most once. That is what bounds the stack at cellCount: mark-on-pop would let several
            // neighbours push the same cell before any of them pops it, and the stack could overflow.
            stack = new int[cellCount];
        }

        /// <summary>
        /// Rebuilds group data for the whole board. Cheap enough at this scale to run after every
        /// change, which is what keeps the board from ever being observed with stale groups.
        /// </summary>
        public void Recalculate(ReadOnlySpan<Cell> cells)
        {
            AssertMatchesBoard(cells);

            // Fill rather than Clear because 0 is a valid group id and "no group" has to be distinct
            // from "group zero". Paying a loop instead of a memset for 100 ints is not a real cost;
            // conflating the two would be a real bug.
            for (int i = 0; i < groupIdOf.Length; i++) groupIdOf[i] = NoGroup;

            GroupCount = 0;
            LargestGroupSize = 0;

            for (int seed = 0; seed < cells.Length; seed++)
            {
                if (!cells[seed].IsColor) continue;      // Empty and Box cells belong to no group
                if (groupIdOf[seed] != NoGroup) continue; // already swallowed by an earlier component

                int groupId = GroupCount++;
                byte color = cells[seed].Color;
                int size = 0;

                int top = 0;
                stack[top++] = seed;
                groupIdOf[seed] = groupId;

                while (top > 0)
                {
                    int current = stack[--top];
                    size++;

                    for (int direction = 0; direction < Grid.DirectionCount; direction++)
                    {
                        if (!Grid.TryStep(current, direction, rows, cols, out int neighbor)) continue;
                        if (groupIdOf[neighbor] != NoGroup) continue;

                        Cell cell = cells[neighbor];
                        if (!cell.IsColor || cell.Color != color) continue;

                        groupIdOf[neighbor] = groupId;   // mark on push, see the stack field
                        stack[top++] = neighbor;
                    }
                }

                groupSizes[groupId] = size;
                if (size > LargestGroupSize) LargestGroupSize = size;
            }
        }

        /// <summary>Group id of a cell, or -1 for Empty and Box cells.</summary>
        public int GroupIdAt(int cellIndex) => groupIdOf[cellIndex];

        /// <summary>Size of the group a cell belongs to, or 0 if it belongs to none.</summary>
        public int GroupSizeAt(int cellIndex)
        {
            int groupId = groupIdOf[cellIndex];
            return groupId == NoGroup ? 0 : groupSizes[groupId];
        }

        /// <summary>Size of a group by id. Valid for ids below <see cref="GroupCount"/>.</summary>
        public int SizeOfGroup(int groupId) => groupSizes[groupId];

        /// <summary>
        /// Whether tapping this cell would blast something. A lone coloured cell still gets a group id
        /// of its own - that keeps "no group" meaning exactly one thing - so being blastable is a
        /// question about size, asked separately.
        /// </summary>
        public bool IsBlastable(int cellIndex)
        {
            int groupId = groupIdOf[cellIndex];
            return groupId != NoGroup && groupSizes[groupId] >= MinBlastableSize;
        }


        /// <summary>
        /// Which icon a cell shows: 0 default, 1/2/3 the A/B/C icons. Empty and Box cells report 0.
        /// </summary>
        /// <remarks>
        /// Derived on read rather than stored. The group data behind it costs a full flood fill and is
        /// worth caching; this is three comparisons and is not. Keeping it as a fourth array would only
        /// add a value that has to be kept in sync, and getting that wrong shows up as a wrong sprite -
        /// a bug nobody notices by looking at the board.
        /// <para>
        /// The comparisons are strictly greater and run high to low, so a size equal to a threshold
        /// stays on the tier below it. Example 1 in the case document contradicts itself here (it lists
        /// C=9 and then says "more than 10"); Example 2 is consistent, so &gt; C is the rule.
        /// </para>
        /// </remarks>
        public int TierAt(int cellIndex)
        {
            int groupId = groupIdOf[cellIndex];
            if (groupId == NoGroup) return 0;

            int size = groupSizes[groupId];

            if (size > thresholdC) return 3;
            if (size > thresholdB) return 2;
            if (size > thresholdA) return 1;
            return 0;
        }

        /// <summary>
        /// Compiled out of release builds entirely, so the check is free where it matters and present
        /// where it helps. Guards the one seam Karar 13 left open: this class knows the board's
        /// dimensions from its constructor but receives the data separately, so the two could drift.
        /// </summary>
        [Conditional("UNITY_ASSERTIONS")]
        private void AssertMatchesBoard(ReadOnlySpan<Cell> cells)
        {
            if (cells.Length != groupIdOf.Length)
                throw new ArgumentException(
                    $"Board has {cells.Length} cells but this GroupFinder was built for {rows}x{cols}.",
                    nameof(cells));
        }
    }
}