using System;
using System.Diagnostics;

namespace BlastGame.Core
{
    // Connected components of same-coloured cells. Owns its scratch arrays; the board arrives per
    // call as a span and is never retained.
    // One full scan answers three questions: what is blastable, what icon each block shows, whether
    // the board is deadlocked. Incremental would be wrong - one column collapsing can merge or split
    // groups several columns away.
    public sealed class GroupFinder
    {
        public const int MinBlastableSize = 2;

        private const int NoGroup = -1;

        private readonly int rows;
        private readonly int cols;

        private readonly int thresholdA;
        private readonly int thresholdB;
        private readonly int thresholdC;

        private readonly int[] groupIdOf;
        private readonly int[] groupSizes;
        private readonly int[] stack;

        public int GroupCount { get; private set; }

        // Deadlock detection falls out of this for free: no group of MinBlastableSize, no legal move.
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

            // Explicit stack, not recursion: a single-colour 10x10 board is a 100-deep call chain.
            // Marked on PUSH, never on pop, so a cell is pushed at most once - that bounds this at cellCount.
            stack = new int[cellCount];
        }

        public void Recalculate(ReadOnlySpan<Cell> cells)
        {
            AssertMatchesBoard(cells);

            // Fill, not Array.Clear: 0 is a valid group id, so "no group" has to be a different value.
            for (int i = 0; i < groupIdOf.Length; i++) groupIdOf[i] = NoGroup;

            GroupCount = 0;
            LargestGroupSize = 0;

            for (int seed = 0; seed < cells.Length; seed++)
            {
                if (!cells[seed].IsColor) continue;
                if (groupIdOf[seed] != NoGroup) continue;

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

                        groupIdOf[neighbor] = groupId;   // mark on push
                        stack[top++] = neighbor;
                    }
                }

                groupSizes[groupId] = size;
                if (size > LargestGroupSize) LargestGroupSize = size;
            }
        }

        public int GroupIdAt(int cellIndex) => groupIdOf[cellIndex];

        public int GroupSizeAt(int cellIndex)
        {
            int groupId = groupIdOf[cellIndex];
            return groupId == NoGroup ? 0 : groupSizes[groupId];
        }

        public int SizeOfGroup(int groupId) => groupSizes[groupId];

        // A lone coloured cell still gets a group id of its own, so being blastable is a question
        // about size, asked separately.
        public bool IsBlastable(int cellIndex)
        {
            int groupId = groupIdOf[cellIndex];
            return groupId != NoGroup && groupSizes[groupId] >= MinBlastableSize;
        }

        // 0 default, 1/2/3 the A/B/C icons. Derived on read rather than stored: three comparisons over
        // data we already have, and a cached copy would go stale as a wrong sprite.
        // Strictly greater, high to low, so a size equal to a threshold stays on the tier below.
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

        // This class knows the board's dimensions from its constructor but receives the data
        // separately, so the two could drift. Compiled out of release builds.
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
