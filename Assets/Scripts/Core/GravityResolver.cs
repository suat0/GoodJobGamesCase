using System;
using System.Diagnostics;

namespace BlastGame.Core
{
    // Collapses each column into the gaps below it and refills the top with new blocks.
    // The board arrives per call as a span and is never retained; no scratch arrays - a column
    // collapses in place behind a single write cursor.
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

        // Boxes are walls: a column is a stack of segments that each collapse within themselves. Only
        // the top segment refills, so a gap under a Box can stay empty for the rest of the level - that
        // is correct, and it never locks the column because the topmost Box can always be damaged.
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
                        // The segment below is finished; the next one starts above the Box.
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

                // Order matters: the block landing lowest is the one that entered first.
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

        // The refill loop assumes the top segment reaches the top row, which holds only because Boxes
        // never spawn there and never move. Otherwise a column would silently stop producing blocks.
        [Conditional("UNITY_ASSERTIONS")]
        private void AssertTopCellIsNotBox(Span<Cell> cells, int col)
        {
            if (cells[(rows - 1) * cols + col].IsBox)
                throw new InvalidOperationException(
                    $"Column {col} has a Box on the top row; generation must never place one there.");
        }
    }
}
