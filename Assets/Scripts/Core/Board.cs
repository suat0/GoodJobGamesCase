using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BlastGame.Core
{
    /// <summary>
    /// Owns the board's cells and the index arithmetic over them. Engine-free by construction: this
    /// assembly is compiled with "No Engine References", so nothing here can reach for UnityEngine.
    /// </summary>
    /// <remarks>
    /// <b>Row 0 is the bottom row</b> (see <see cref="Grid"/>). Gravity falls toward decreasing index.
    /// <para>
    /// The cell array is <c>readonly</c> and never reassigned or resized after construction. Algorithms
    /// receive it as a span per call rather than holding it (DECISIONS.md, Karar 13), so this is the
    /// single owner of board data.
    /// </para>
    /// </remarks>
    public sealed class Board
    {
        private readonly Cell[] cells;

        /// <summary>
        /// Randomness is injected, never created here: board generation and shuffle must be
        /// reproducible from a seed for tests and for "give me that board again" debugging.
        /// A class that makes its own randomness cannot be tested deterministically.
        /// </summary>
        private readonly Random rng;

        /// <summary>
        /// Rebuilt after every board change so group data is never observed stale. Created here rather
        /// than injected: there will only ever be one implementation, and Board is the only thing that
        /// knows when the cells changed.
        /// </summary>
        private readonly GroupFinder groupFinder;

        public int Rows { get; }
        public int Cols { get; }
        public int CellCount => cells.Length;

        /// <param name="rng">
        /// Owned by the caller. <c>new Random(seed)</c> for a reproducible board, <c>new Random()</c>
        /// for a fresh one.
        /// </param>
        public Board(int rows, int cols, Random rng)
        {
            // The 2-10 range the case document specifies is an authoring constraint and is enforced
            // in LevelConfig. Core only rejects what it genuinely cannot represent, so an oversized
            // board still runs - which is also what makes the document's own N=12 example playable.
            if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows), rows, "Board needs at least one row.");
            if (cols < 1) throw new ArgumentOutOfRangeException(nameof(cols), cols, "Board needs at least one column.");

            this.rng = rng ?? throw new ArgumentNullException(nameof(rng));

            Rows = rows;
            Cols = cols;

            // CellType.Empty is 0, so this is already a valid empty board - no initialisation pass.
            cells = new Cell[rows * cols];

            groupFinder = new GroupFinder(rows, cols);
        }

        /// <summary>
        /// Fills the board: every cell gets a random colour, then <paramref name="boxCount"/> Boxes are
        /// scattered over it. Safe to call again to restart a level - every cell is overwritten.
        /// </summary>
        /// <remarks>
        /// <b>No Box on the top row.</b> Not in the case document; we added it. It guarantees that every
        /// column can receive falling blocks, which makes the top row always full, which means there are
        /// always at least two adjacent coloured cells, which is what shuffle needs to be able to work.
        /// One placement rule buys the whole chain (DECISIONS.md, Karar 8).
        /// </remarks>
        /// <param name="boxCount">
        /// A request, not a promise: silently clamped to what the board can hold under the rule above.
        /// Nothing reads this number afterwards - the objective counts live Boxes on the board instead,
        /// so a clamp cannot make a level unwinnable.
        /// </param>
        public void Generate(int colorCount, int boxCount)
        {
            // Colours are stored in a byte, so the range has to fit one.
            if (colorCount < 1 || colorCount > 256)
                throw new ArgumentOutOfRangeException(nameof(colorCount), colorCount, "Colour count must be in [1, 256].");
            if (boxCount < 0)
                throw new ArgumentOutOfRangeException(nameof(boxCount), boxCount, "Box count cannot be negative.");

            // Uniform per cell, not a balanced deck. A deck would even out the starting colours, but
            // refills after the first blast are uniform anyway, so the guarantee would hold for exactly
            // one move and then quietly stop being true. K=1 is legal and needs no special case: the
            // whole board becomes one group, which is correct.
            for (int i = 0; i < cells.Length; i++)
                cells[i] = Cell.MakeColor((byte)rng.Next(colorCount));

            // Row 0 is the bottom and the array is row-major, so the top row is the LAST Cols entries
            // and the Box-eligible cells are the contiguous prefix [0, boxCapacity). The row convention
            // means the constraint costs no filtering at all.
            int boxCapacity = (Rows - 1) * Cols;
            int toPlace = Math.Min(boxCount, boxCapacity);
            if (toPlace == 0) return;

            var candidates = new int[boxCapacity];
            for (int i = 0; i < boxCapacity; i++) candidates[i] = i;

            // Partial Fisher-Yates. The full shuffle settles every position; we only need the first
            // toPlace of them, so the loop stops early - exactly toPlace steps, no retries, no chance
            // of picking the same cell twice.
            //
            // This runs front-to-back while the canonical form in Karar 9 runs back-to-front. Same
            // algorithm from opposite ends: what makes either one unbiased is that the random index is
            // drawn only from the region not yet fixed. Drawing from the whole range every time -
            // rng.Next(0, n) - is the classic broken version, because n^n draws cannot divide evenly
            // into n! permutations.
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

        /// <summary>
        /// Refreshes group data for the current cells. Every mutation of the board ends with this, so
        /// callers never have to remember to ask.
        /// </summary>
        public void RecalculateGroups() => groupFinder.Recalculate(cells);

        /// <summary>Size of the group a cell belongs to; 0 for Empty and Box cells.</summary>
        public int GroupSizeAt(int index) => groupFinder.GroupSizeAt(index);

        /// <summary>Whether tapping this cell would blast a group.</summary>
        public bool IsBlastable(int index) => groupFinder.IsBlastable(index);

        /// <summary>Biggest group on the board. Zero when no coloured cells remain.</summary>
        public int LargestGroupSize => groupFinder.LargestGroupSize;

        /// <summary>
        /// Live Boxes on the board. Counted rather than tracked: a decrementing counter would be a third
        /// piece of state to keep in sync, and getting it wrong fails silently - the level just never
        /// ends, or ends early. Once per move over 100 cells is not worth a bug class.
        /// </summary>
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

        /// <summary>Neighbour of a cell in one of the four orthogonal directions, or false at the edge.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryNeighbor(int index, int direction, out int neighbor)
            => Grid.TryStep(index, direction, Rows, Cols, out neighbor);

        /// <summary>
        /// Reads a cell by value. Three bytes, so the copy is free - and it is also the point: callers
        /// outside Core get a snapshot they cannot write back through.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cell CellAt(int index) => cells[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cell CellAt(int row, int col) => cells[Index(row, col)];

        /// <summary>
        /// Renders the board the way a person looks at it: the top row first, row 0 last. Test failure
        /// output and debug dumps only; nothing in the game loop calls this.
        /// </summary>
        public override string ToString()
        {
            // Two chars per cell plus a separator, plus one newline per row.
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