using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BlastGame.Core
{
    // Owns the board's cells. Row 0 is the BOTTOM row, so gravity falls toward decreasing index.
    // The array is never reassigned or resized, and the algorithms below receive it as a span per call
    // rather than holding it - so this stays the single owner of board data.
    public sealed class Board
    {
        private readonly Cell[] cells;

        private readonly Random rng;

        private readonly GroupFinder groupFinder;
        private readonly GravityResolver gravity;

        // Built up front even though it is only used on a deadlocked board - allocating its scratch
        // arrays on demand would spike the frame already doing the most work.
        private readonly DeadlockResolver deadlockResolver;

        private readonly BlastResult lastBlast;

        // A Box takes one damage per adjacent GROUP blasted, not per neighbouring block. blastStamp is
        // bumped once per blast, so old marks go stale by themselves and nothing needs clearing.
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

            cells = new Cell[Rows * Cols];   // CellType.Empty is 0, so this is already valid

            groupFinder = new GroupFinder(config);
            gravity = new GravityResolver(config, this.rng);
            deadlockResolver = new DeadlockResolver(config, this.rng);
            lastBlast = new BlastResult(config);
            boxStamp = new int[cells.Length];
        }

        // One rule carries a lot here: no Box on the top row. Every column can then receive falling
        // blocks, so the top row is always full, so two adjacent coloured cells always exist, so the
        // shuffle always has somewhere to place a group.
        public void Generate()
        {
            // Uniform per cell. Refills are uniform anyway, so a balanced deck would hold for exactly
            // one move.
            for (int i = 0; i < cells.Length; i++)
                cells[i] = Cell.MakeColor((byte)rng.Next(config.ColorCount));

            PlaceBoxes();

            // Unconditional. An early return inside PlaceBoxes once skipped this, and a board whose
            // groups were never scanned reports every cell unblastable and the whole board deadlocked.
            RecalculateGroups();

            GuaranteeALegalMove();
        }

        private void PlaceBoxes()
        {
            int boxCapacity = (Rows - 1) * Cols;
            int toPlace = Math.Min(config.BoxCount, boxCapacity);
            if (toPlace == 0) return;

            var candidates = new int[boxCapacity];
            for (int i = 0; i < boxCapacity; i++) candidates[i] = i;

            for (int i = 0; i < toPlace; i++)
            {
                int j = i + rng.Next(boxCapacity - i);   // untouched tail only

                int pick = candidates[j];
                candidates[j] = candidates[i];
                candidates[i] = pick;

                cells[pick] = Cell.MakeBox();
            }
        }

        // Small boards make a move-less board ordinary rather than exotic: a 2x2 holding one Box has
        // three coloured cells, and with six colours all three come out different more often than not.
        //
        // Generation can fix what the shuffle cannot. The shuffle swaps, so it needs a colour that
        // already occurs twice; this assigns, so one write is enough and it cannot fail.
        //
        // The column is drawn rather than fixed at zero, so the guaranteed pair does not always land in
        // the same corner. A single-column board has no horizontal neighbour and is left alone -
        // GameSession reports that as a level already over.
        private void GuaranteeALegalMove()
        {
            if (Cols < 2 || !IsDeadlocked) return;

            int row = Rows - 1;
            int col = rng.Next(Cols - 1);

            cells[Index(row, col + 1)] = Cell.MakeColor(cells[Index(row, col)].Color);

            RecalculateGroups();
        }

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
        // Damage lands before gravity, so a Box breaking this move has its cell filled in the same one.
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
            // step with the scan, for nothing measurable at 100 cells.
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
                cells[neighbor].Health--;                          // in place; see the Cell copy trap

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

        public void RecalculateGroups() => groupFinder.Recalculate(cells);

        public int GroupSizeAt(int index) => groupFinder.GroupSizeAt(index);

        public int TierAt(int index) => groupFinder.TierAt(index);

        public bool IsBlastable(int index) => groupFinder.IsBlastable(index);

        public int LargestGroupSize => groupFinder.LargestGroupSize;

        // Free - the group scan already knows the largest group, so this is one comparison.
        public bool IsDeadlocked => groupFinder.LargestGroupSize < GroupFinder.MinBlastableSize;

        // Deliberately not called from TryBlast. A board that has just been won must not shuffle, and
        // that ordering is a game rule, so Core offers the remedy and the caller decides.
        public bool TryResolveDeadlock()
        {
            if (!deadlockResolver.TryResolve(cells)) return false;

            RecalculateGroups();
            return true;
        }

        // Counted rather than tracked - a counter would be a third piece of state, and its drift would
        // fail silently.
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cell CellAt(int index) => cells[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cell CellAt(int row, int col) => cells[Index(row, col)];

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
