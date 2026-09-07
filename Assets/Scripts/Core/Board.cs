using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BlastGame.Core
{
    // Owns the board's cells. Row 0 is the BOTTOM row, so gravity falls toward decreasing index.
    // The cell array is never reassigned or resized; algorithms receive it as a span per call rather
    // than holding it, so this is the single owner of board data.
    public sealed class Board
    {
        private readonly Cell[] cells;

        // Injected, never created here: generation and shuffle must be reproducible from a seed.
        private readonly Random rng;

        private readonly GroupFinder groupFinder;
        private readonly GravityResolver gravity;

        // Built up front although it is only used on a deadlocked board: its scratch arrays are sized
        // from the board, and allocating them on demand would spike the frame already doing the most work.
        private readonly DeadlockResolver deadlockResolver;

        private readonly BlastResult lastBlast;

        // A Box takes one damage per adjacent GROUP blasted, not per neighbouring block. The stamp is
        // compared against blastStamp, which is incremented once per blast - so old marks go stale by
        // themselves and there is nothing to clear between moves.
        private readonly int[] boxStamp;
        private int blastStamp;

        private readonly BoardConfig config;

        public int Rows { get; }
        public int Cols { get; }
        public int CellCount => cells.Length;

        public Board(BoardConfig config, Random rng)
        {
            config.Validate();

            this.config = config;
            this.rng = rng ?? throw new ArgumentNullException(nameof(rng));

            Rows = config.Rows;
            Cols = config.Cols;

            // CellType.Empty is 0, so this is already a valid empty board.
            cells = new Cell[Rows * Cols];

            groupFinder = new GroupFinder(config);
            gravity = new GravityResolver(config, this.rng);
            deadlockResolver = new DeadlockResolver(config, this.rng);
            lastBlast = new BlastResult(config);
            boxStamp = new int[cells.Length];
        }

        // No Box on the top row: every column can then receive falling blocks, so the top row is
        // always full, so two adjacent coloured cells always exist, so shuffle can always work.
        // BoxCount is a request, clamped to what the board can hold under that rule.
        public void Generate()
        {
            int colorCount = config.ColorCount;
            int boxCount = config.BoxCount;

            // Uniform per cell, not a balanced deck: refills are uniform anyway, so a balanced start
            // would hold for exactly one move.
            for (int i = 0; i < cells.Length; i++)
                cells[i] = Cell.MakeColor((byte)rng.Next(colorCount));

            // Row 0 is the bottom and the array is row-major, so the Box-eligible cells are the
            // contiguous prefix - the top-row rule costs no filtering.
            int boxCapacity = (Rows - 1) * Cols;
            int toPlace = Math.Min(boxCount, boxCapacity);
            if (toPlace == 0) return;

            var candidates = new int[boxCapacity];
            for (int i = 0; i < boxCapacity; i++) candidates[i] = i;

            // Partial Fisher-Yates: only the first toPlace positions need settling, and the random
            // index is drawn only from the region not yet fixed.
            for (int i = 0; i < toPlace; i++)
            {
                int j = i + rng.Next(boxCapacity - i);   // untouched tail only

                int pick = candidates[j];
                candidates[j] = candidates[i];
                candidates[i] = pick;

                cells[pick] = Cell.MakeBox();
            }

            RecalculateGroups();
        }

        // The counterpart to Generate: same contract, layout stated instead of rolled. Tests describe a
        // situation exactly, and a hand-authored level would enter the same way.
        public void LoadState(ReadOnlySpan<Cell> state)
        {
            if (state.Length != cells.Length)
                throw new ArgumentException(
                    $"Expected {cells.Length} cells for a {Rows}x{Cols} board, got {state.Length}.",
                    nameof(state));

            state.CopyTo(cells);
            RecalculateGroups();
        }

        public BlastResult LastBlast => lastBlast;

        // Leaves the board in its final state: removed, damaged, settled, refilled, groups rebuilt.
        // Never observable mid-move, which is what makes the view's animation purely cosmetic.
        // Damage lands before gravity so a Box breaking this move has its cell filled in the same move.
        public bool TryBlast(int index)
        {
            if (!InBounds(index)) return false;
            if (!groupFinder.IsBlastable(index)) return false;

            int groupId = groupFinder.GroupIdAt(index);

            lastBlast.Clear();
            lastBlast.TappedIndex = index;
            lastBlast.BlastedGroupSize = groupFinder.SizeOfGroup(groupId);

            blastStamp++;

            // O(cells) where a stored member list would be O(group) - another structure to keep in
            // step with the scan, for no measurable gain at 100 cells.
            for (int i = 0; i < cells.Length; i++)
            {
                if (groupFinder.GroupIdAt(i) != groupId) continue;

                cells[i] = Cell.Empty;
                lastBlast.AddRemoved(i);
                DamageAdjacentBoxes(i);
            }

            gravity.Apply(cells, lastBlast);
            RecalculateGroups();

            return true;
        }

        private void DamageAdjacentBoxes(int index)
        {
            for (int direction = 0; direction < Grid.DirectionCount; direction++)
            {
                if (!TryNeighbor(index, direction, out int neighbor)) continue;
                if (!cells[neighbor].IsBox) continue;
                if (boxStamp[neighbor] == blastStamp) continue;   // already hit by this same blast

                boxStamp[neighbor] = blastStamp;
                cells[neighbor].Health--;                          // in place, never through a copy

                if (cells[neighbor].Health == 0)
                {
                    cells[neighbor] = Cell.Empty;
                    lastBlast.AddBrokenBox(neighbor);
                }
                else
                {
                    lastBlast.AddDamagedBox(neighbor);
                }
            }
        }

        // Every mutation of the board ends with this, so callers never have to remember to ask.
        public void RecalculateGroups() => groupFinder.Recalculate(cells);

        public int GroupSizeAt(int index) => groupFinder.GroupSizeAt(index);

        public int TierAt(int index) => groupFinder.TierAt(index);

        public bool IsBlastable(int index) => groupFinder.IsBlastable(index);

        public int LargestGroupSize => groupFinder.LargestGroupSize;

        // Free: the group scan already knows the largest group, so this is a comparison, not a pass.
        public bool IsDeadlocked => groupFinder.LargestGroupSize < GroupFinder.MinBlastableSize;

        // Deliberately not called from TryBlast: a board that has just been won must not shuffle, and
        // that ordering is a game rule. Core offers the remedy, the caller decides.
        public bool TryResolveDeadlock()
        {
            if (!deadlockResolver.TryResolve(cells)) return false;

            RecalculateGroups();
            return true;
        }

        // Counted, not tracked: a counter is a third piece of state whose drift fails silently.
        public int RemainingBoxes()
        {
            int count = 0;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].IsBox) count++;
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Index(int row, int col) => Grid.Index(row, col, Cols);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int RowOf(int index) => Grid.RowOf(index, Cols);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ColOf(int index) => Grid.ColOf(index, Cols);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InBounds(int row, int col) => row >= 0 && row < Rows && col >= 0 && col < Cols;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InBounds(int index) => index >= 0 && index < cells.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryNeighbor(int index, int direction, out int neighbor)
            => Grid.TryStep(index, direction, Rows, Cols, out neighbor);

        // By value. Three bytes, so the copy is free - and it is also the point: callers outside Core
        // get a snapshot they cannot write back through.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cell CellAt(int index) => cells[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cell CellAt(int row, int col) => cells[Index(row, col)];

        // Top row first, row 0 last - the way a person looks at it. Test output and debug dumps only.
        public override string ToString()
        {
            var sb = new StringBuilder(Rows * (Cols * 3 + 1));

            for (int row = Rows - 1; row >= 0; row--)
            {
                for (int col = 0; col < Cols; col++)
                {
                    sb.Append(cells[Index(row, col)].ToString().PadRight(3));
                }
                sb.Append('\n');
            }

            return sb.ToString();
        }
    }
}
