using System;
using System.Diagnostics;

namespace BlastGame.Core
{
    /// <summary>
    /// Collapses each column into the gaps below it and refills the top with new blocks.
    /// </summary>
    /// <remarks>
    /// Shaped like <see cref="GroupFinder"/>: the board arrives per call as a span and is never retained
    /// (Karar 13). It writes as well as reads, so the span is mutable - but the ownership story is the
    /// same, and <see cref="Random"/> is injected rather than created, so a seeded board replays exactly.
    /// <para>
    /// No scratch arrays: a column collapses in place behind a single write cursor.
    /// </para>
    /// </remarks>
    public sealed class GravityResolver
    {
        private readonly int rows;
        private readonly int cols;
        private readonly int colorCount;
        private readonly Random rng;

        public GravityResolver(BoardConfig config, Random rng)
        {
            config.Validate();

            rows = config.Rows;
            cols = config.Cols;
            colorCount = config.ColorCount;

            this.rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>
        /// Settles every column and records each block that moved into <paramref name="result"/>.
        /// </summary>
        /// <remarks>
        /// <b>Boxes are walls.</b> A Box does not fall and does not let anything past it, so a column is
        /// really a stack of independent segments separated by Boxes. Each one collapses within itself.
        /// <para>
        /// <b>Only the top segment refills.</b> New blocks come from above the board, so the first Box
        /// they meet stops them. A gap under a Box therefore stays empty, sometimes for the rest of the
        /// level. That is correct behaviour, not a bug: it never locks the board, because the topmost Box
        /// in a column always has a full cell above it, so it can always be damaged, and breaking it
        /// reopens the column (DECISIONS.md, Karar 8).
        /// </para>
        /// </remarks>
        public void Apply(Span<Cell> cells, BlastResult result)
        {
            AssertMatchesBoard(cells);

            for (int col = 0; col < cols; col++)
            {
                // Row 0 is the bottom, so walking upward means walking with the array.
                int write = 0;

                for (int row = 0; row < rows; row++)
                {
                    int index = row * cols + col;
                    Cell cell = cells[index];

                    if (cell.IsBox)
                    {
                        // The segment below this Box is finished; the next one starts above it.
                        write = row + 1;
                        continue;
                    }

                    if (!cell.IsColor) continue;   // a hole: leave the write cursor where it is

                    if (write != row)
                    {
                        int target = write * cols + col;
                        cells[target] = cell;
                        cells[index] = Cell.Empty;
                        result.AddMove(index, target);
                    }

                    write++;
                }

                AssertTopCellIsNotBox(cells, col);

                // Whatever is left of the top segment is filled from outside the board. Order matters:
                // the block landing lowest is the one that entered first, so it starts lowest.
                for (int row = write; row < rows; row++)
                {
                    int target = row * cols + col;
                    cells[target] = Cell.MakeColor((byte)rng.Next(colorCount));
                    result.AddMove(result.SpawnSource(col, row - write), target);
                }
            }
        }

        [Conditional("UNITY_ASSERTIONS")]
        private void AssertMatchesBoard(Span<Cell> cells)
        {
            if (cells.Length != rows * cols)
                throw new ArgumentException(
                    $"Board has {cells.Length} cells but this GravityResolver was built for {rows}x{cols}.",
                    nameof(cells));
        }

        /// <summary>
        /// The refill loop assumes the top segment reaches the top row, which holds only because Boxes
        /// never spawn on the top row and never move. If that ever stops being true, a column would
        /// silently stop producing blocks - so it is checked rather than trusted.
        /// </summary>
        [Conditional("UNITY_ASSERTIONS")]
        private void AssertTopCellIsNotBox(Span<Cell> cells, int col)
        {
            if (cells[(rows - 1) * cols + col].IsBox)
                throw new InvalidOperationException(
                    $"Column {col} has a Box on the top row; generation must never place one there.");
        }
    }
}