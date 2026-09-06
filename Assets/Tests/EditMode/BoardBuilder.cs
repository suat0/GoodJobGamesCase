using System;
using BlastGame.Core;

namespace BlastGame.Tests
{
    /// <summary>
    /// Builds board data from string literals so a test's input can be read as a picture.
    /// </summary>
    /// <remarks>
    /// Rows are written top-first, the way a person looks at a board, and reversed on the way in -
    /// row 0 is the bottom row everywhere in Core. Keeping the flip here means no test ever has to
    /// think about it, which is the point: a test that is hard to read cannot be trusted when it fails.
    /// <para>
    /// Produces a raw <c>Cell[]</c> rather than a <see cref="Board"/>. That is not a workaround - it is
    /// Karar 13/A2 paying off. <see cref="GroupFinder"/> takes its data per call, so it can be exercised
    /// on a hand-written board without constructing anything else.
    /// </para>
    /// </remarks>
    public static class BoardBuilder
    {
        public const char Empty = '.';
        public const char FullBox = '#';
        public const char DamagedBox = 'x';

        /// <summary>
        /// <c>0-9</c> a colour index, <c>#</c> a Box at full health, <c>x</c> a Box with one health left,
        /// <c>.</c> an empty cell.
        /// </summary>
        public static TestBoard Parse(params string[] rowsTopFirst)
        {
            if (rowsTopFirst == null || rowsTopFirst.Length == 0)
                throw new ArgumentException("A board needs at least one row.", nameof(rowsTopFirst));

            int rows = rowsTopFirst.Length;
            int cols = rowsTopFirst[0].Length;

            var cells = new Cell[rows * cols];

            for (int row = 0; row < rows; row++)
            {
                string line = rowsTopFirst[rows - 1 - row];   // top-first in, bottom-first out

                if (line.Length != cols)
                    throw new ArgumentException(
                        $"Row '{line}' has {line.Length} cells but the first row has {cols}.");

                for (int col = 0; col < cols; col++)
                    cells[row * cols + col] = ParseCell(line[col]);
            }

            return new TestBoard(cells, rows, cols);
        }

        private static Cell ParseCell(char symbol)
        {
            if (symbol == Empty) return Cell.Empty;
            if (symbol == FullBox) return Cell.MakeBox();
            if (symbol == DamagedBox) return Cell.MakeBox(1);
            if (symbol >= '0' && symbol <= '9') return Cell.MakeColor((byte)(symbol - '0'));

            throw new ArgumentException($"Unknown board symbol '{symbol}'.");
        }
    }

    /// <summary>Hand-written board data plus the dimensions that go with it.</summary>
    public readonly struct TestBoard
    {
        public readonly Cell[] Cells;
        public readonly int Rows;
        public readonly int Cols;

        public TestBoard(Cell[] cells, int rows, int cols)
        {
            Cells = cells;
            Rows = rows;
            Cols = cols;
        }

        /// <summary>Row 0 is the bottom row, matching Core.</summary>
        public int Index(int row, int col) => row * Cols + col;

        /// <summary>
        /// Runs a scan over this board and hands back the finder to query. Thresholds default to
        /// Example 1 in the case document (A=4, B=7, C=9).
        /// </summary>
        public GroupFinder Analyze(int thresholdA = 4, int thresholdB = 7, int thresholdC = 9)
        {
            var config = new BoardConfig(Rows, Cols, 10, thresholdA, thresholdB, thresholdC, 0);
            var finder = new GroupFinder(config);
            finder.Recalculate(Cells);
            return finder;
        }
    }
}