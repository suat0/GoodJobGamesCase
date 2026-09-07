using BlastGame.Core;
using NUnit.Framework;

namespace BlastGame.Tests
{
    /// <summary>
    /// Adjacency, minimum group size and row wrapping. The criterion for picking these was "would I
    /// notice if it broke silently?" - all three fail in ways the screen does not show.
    /// </summary>
    [TestFixture]
    public class GroupFinderTests
    {
        // --- Test 3: minimum group size is 2 ------------------------------------------------------

        [Test]
        public void LoneCell_HasItsOwnGroup_ButIsNotBlastable()
        {
            var board = BoardBuilder.Parse(
                "01",
                "11");

            var groups = board.Analyze();
            int lone = board.Index(1, 0);   // the single 0, top-left

            Assert.AreEqual(1, groups.GroupSizeAt(lone), "a lone coloured cell still forms a group of one");
            Assert.IsFalse(groups.IsBlastable(lone), "a group of one must not be blastable");
        }

        [Test]
        public void PairOfSameColour_IsBlastable()
        {
            var board = BoardBuilder.Parse(
                "01",
                "11");

            var groups = board.Analyze();

            Assert.IsTrue(groups.IsBlastable(board.Index(0, 0)), "three adjacent 1s are blastable");
            Assert.AreEqual(3, groups.GroupSizeAt(board.Index(0, 0)));
        }

        [Test]
        public void BoxAndEmptyCells_BelongToNoGroup()
        {
            var board = BoardBuilder.Parse(
                "1#1",
                "1.1");

            var groups = board.Analyze();

            Assert.AreEqual(-1, groups.GroupIdAt(board.Index(1, 1)), "a Box belongs to no group");
            Assert.AreEqual(0, groups.GroupSizeAt(board.Index(1, 1)));
            Assert.AreEqual(-1, groups.GroupIdAt(board.Index(0, 1)), "an empty cell belongs to no group");
            Assert.IsFalse(groups.IsBlastable(board.Index(0, 1)));
        }

        // --- Test 4: adjacency is orthogonal ------------------------------------------------------

        [Test]
        public void DiagonalNeighbours_AreNotOneGroup()
        {
            var board = BoardBuilder.Parse(
                "10",
                "01");

            var groups = board.Analyze();

            Assert.AreEqual(4, groups.GroupCount, "four diagonally-touching cells are four groups");
            Assert.AreEqual(1, groups.LargestGroupSize, "no group forms across a diagonal");
            Assert.AreNotEqual(groups.GroupIdAt(board.Index(1, 0)), groups.GroupIdAt(board.Index(0, 1)),
                "the two 1s touch only at a corner");
        }

        [Test]
        public void OrthogonalNeighbours_AreOneGroup_AroundAnObstacle()
        {
            //  1 1 0 0
            //  1 # 0 0     the 0s reach around the Box; the bottom-left 0s are cut off from them
            //  0 0 1 1
            var board = BoardBuilder.Parse(
                "1100",
                "1#00",
                "0011");

            var groups = board.Analyze();

            Assert.AreEqual(3, groups.GroupSizeAt(board.Index(2, 0)), "the 1s form an L of three");
            Assert.AreEqual(4, groups.GroupSizeAt(board.Index(2, 2)), "the 0s above the Box are four");
            Assert.AreEqual(2, groups.GroupSizeAt(board.Index(0, 0)), "the bottom-left 0s are a separate pair");
            Assert.AreNotEqual(groups.GroupIdAt(board.Index(2, 2)), groups.GroupIdAt(board.Index(0, 0)),
                "same colour, but no orthogonal path between them");

            int total = 0;
            for (int id = 0; id < groups.GroupCount; id++) total += groups.SizeOfGroup(id);
            Assert.AreEqual(11, total, "group sizes must account for every coloured cell exactly once");
        }

        // --- Test 9: no wrap-around at row boundaries ---------------------------------------------

        [Test]
        public void RowEnd_IsNotAdjacentTo_NextRowStart()
        {
            // Indices 1 and 2 sit next to each other in the array but on opposite edges of the board.
            // Treating a 1D index as adjacent to index +- 1 would silently merge them.
            //
            //  5 1     <- row 1: index 2, index 3
            //  0 5     <- row 0: index 0, index 1
            var board = BoardBuilder.Parse(
                "51",
                "05");

            var groups = board.Analyze();

            Assert.AreNotEqual(groups.GroupIdAt(1), groups.GroupIdAt(2),
                "the last cell of a row and the first of the next are not neighbours");
            Assert.AreEqual(1, groups.LargestGroupSize, "no group may form across the row boundary");
        }

        [Test]
        public void WideBoard_KeepsRowsSeparate()
        {
            // Every row is a solid run of one colour, and neighbouring rows differ. If wrapping were
            // possible the rows would fuse into one long group.
            var board = BoardBuilder.Parse(
                "22222",
                "11111",
                "22222");

            var groups = board.Analyze();

            Assert.AreEqual(3, groups.GroupCount, "three rows, three groups");
            Assert.AreEqual(5, groups.LargestGroupSize, "each group is exactly one row wide");
        }

        // --- scale and stability ------------------------------------------------------------------

        [Test]
        public void SingleColourBoard_IsOneGroup_WithoutOverflowingTheStack()
        {
            // The worst case for the DFS stack: every cell reachable from every other. This passes only
            // because cells are marked when pushed rather than when popped.
            var config = new BoardConfig(10, 10, 1, 4, 7, 9, 0);
            var cells = new Cell[100];
            for (int i = 0; i < cells.Length; i++) cells[i] = Cell.MakeColor(0);

            var groups = new GroupFinder(config);
            groups.Recalculate(cells);

            Assert.AreEqual(1, groups.GroupCount);
            Assert.AreEqual(100, groups.LargestGroupSize);
        }

        [Test]
        public void Checkerboard_LeavesNoBlastableGroup()
        {
            // The deadlock signal Faz 3 will read: the largest group on the board is below the minimum.
            var config = new BoardConfig(10, 10, 2, 4, 7, 9, 0);
            var cells = new Cell[100];
            for (int row = 0; row < 10; row++)
                for (int col = 0; col < 10; col++)
                    cells[row * 10 + col] = Cell.MakeColor((byte)((row + col) % 2));

            var groups = new GroupFinder(config);
            groups.Recalculate(cells);

            Assert.AreEqual(100, groups.GroupCount);
            Assert.Less(groups.LargestGroupSize, GroupFinder.MinBlastableSize);
        }

        [Test]
        public void RepeatedScans_ProduceTheSameResult()
        {
            // Scratch arrays are reused across calls, so a scan that forgot to reset one would drift.
            var board = BoardBuilder.Parse(
                "1100",
                "1#00",
                "0011");

            var groups = board.Analyze();
            int countAfterFirst = groups.GroupCount;
            int largestAfterFirst = groups.LargestGroupSize;

            for (int i = 0; i < 10; i++) groups.Recalculate(board.Cells);

            Assert.AreEqual(countAfterFirst, groups.GroupCount);
            Assert.AreEqual(largestAfterFirst, groups.LargestGroupSize);
        }
    }
}