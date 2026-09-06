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