using System;
using BlastGame.Core;
using NUnit.Framework;

namespace BlastGame.Tests
{
    [TestFixture]
    public class GravityResolverTests
    {
        private static BoardConfig Config(TestBoard b, int colorCount = 1)
            => new BoardConfig(b.Rows, b.Cols, colorCount, 4, 7, 9, 0);

        // colorCount 1 makes every spawned block colour 0, so the board after gravity is fully
        // predictable and a test can be written as a picture.
        private static string Settle(TestBoard board, out BlastResult result, int seed = 1)
        {
            var config = Config(board);
            var gravity = new GravityResolver(config, new Random(seed));
            result = new BlastResult(config);
            gravity.Apply(board.Cells, result);
            return Render(board);
        }

        private static string Render(TestBoard b)
        {
            var sb = new System.Text.StringBuilder();
            for (int row = b.Rows - 1; row >= 0; row--)
            {
                for (int col = 0; col < b.Cols; col++)
                {
                    Cell c = b.Cells[b.Index(row, col)];
                    sb.Append(c.IsBox ? '#' : c.IsEmpty ? '.' : (char)('0' + c.Color));
                }
                if (row > 0) sb.Append('/');
            }
            return sb.ToString();
        }

        [Test]
        public void BlocksFallIntoHolesBelowThem()
        {
            var board = BoardBuilder.Parse(
                "0",
                ".",
                "0");
            Settle(board, out _);

            Assert.AreEqual("0/0/0", Render(board), "the hole closes and a new block fills the top");
        }

        [Test]
        public void EmptyColumnIsRefilledFromOutside()
        {
            var board = BoardBuilder.Parse(
                ".",
                ".",
                ".");
            Settle(board, out var result);

            Assert.AreEqual("0/0/0", Render(board));
            Assert.AreEqual(3, result.MoveTo.Length, "three blocks entered the column");
            foreach (int from in result.MoveFrom)
                Assert.IsTrue(result.IsSpawn(from), "all three came from outside the board");
        }

        // --- Test 5: a Box splits its column ------------------------------------------------------

        [Test]
        public void BlocksAboveABoxDoNotPassThroughIt()
        {
            var board = BoardBuilder.Parse(
                "0",
                "#",
                ".");
            Settle(board, out _);

            Assert.AreEqual("0/#/.", Render(board),
                "the gap under the Box stays open and nothing crosses the wall");
        }

        [Test]
        public void GapUnderABoxIsPermanentButTheTopSegmentStillRefills()
        {
            //  .        top segment: empty, must refill
            //  #        wall
            //  .        below the wall: unreachable from above
            //  #        wall
            //  .        unreachable
            var board = BoardBuilder.Parse(
                ".",
                "#",
                ".",
                "#",
                ".");
            Settle(board, out var result);

            Assert.AreEqual("0/#/./#/.", Render(board));
            Assert.AreEqual(1, result.MoveTo.Length, "only the top segment received a block");
        }

        [Test]
        public void EachSegmentCollapsesWithinItself()
        {
            //  .        top segment: refills from outside
            //  0        already settled on the wall
            //  #        wall
            //  0        must fall into the hole below it, without leaving its segment
            //  .
            var board = BoardBuilder.Parse(
                ".",
                "0",
                "#",
                "0",
                ".");
            Settle(board, out var result);

            Assert.AreEqual("0/0/#/./0", Render(board),
                "the lower segment compacts downward; the upper one refills from outside");

            bool fellInsideLowerSegment = false;
            for (int i = 0; i < result.MoveTo.Length; i++)
                if (result.MoveFrom[i] == board.Index(1, 0) && result.MoveTo[i] == board.Index(0, 0))
                    fellInsideLowerSegment = true;

            Assert.IsTrue(fellInsideLowerSegment, "the block below the wall fell within its own segment");
            Assert.AreEqual(2, result.MoveTo.Length, "one fall below the wall, one spawn above it");
        }

        [Test]
        public void ColumnsAreIndependent()
        {
            //  1 . 2
            //  . . .
            //  1 # 2     the Box sits on the bottom row, so it blocks nothing above it
            var board = BoardBuilder.Parse(
                "1.2",
                "...",
                "1#2");
            Settle(board, out _, seed: 3);

            Assert.AreEqual("000/102/1#2", Render(board),
                "each column settles on its own; the middle one refills above its Box");
        }

        // --- move bookkeeping ---------------------------------------------------------------------

        [Test]
        public void SettledBlocksAreNotReportedAsMoved()
        {
            var board = BoardBuilder.Parse(
                "0",
                "0");
            Settle(board, out var result);

            Assert.AreEqual(0, result.MoveTo.Length, "a full column has nothing to animate");
        }

        [Test]
        public void SpawnSourcesStackInLandingOrder()
        {
            var board = BoardBuilder.Parse(
                ".",
                ".",
                "0");
            Settle(board, out var result);

            Assert.AreEqual(2, result.MoveTo.Length);

            // The block landing lower must start lower, or the two would overlap in the air.
            int lowLanding = -1, highLanding = -1;
            for (int i = 0; i < result.MoveTo.Length; i++)
            {
                if (result.MoveTo[i] == board.Index(1, 0)) lowLanding = result.MoveFrom[i];
                if (result.MoveTo[i] == board.Index(2, 0)) highLanding = result.MoveFrom[i];
            }

            Assert.Less(lowLanding, highLanding, "spawn sources keep the column's vertical order");
            Assert.AreEqual(3, result.RowOfSource(lowLanding), "the first new block starts one row above the board");
            Assert.AreEqual(0, result.ColOfSource(highLanding), "a spawn source decodes to its own column");
        }

        [Test]
        public void EveryDestinationIsUniqueSoTheResultCannotOverflow()
        {
            var board = BoardBuilder.Parse(
                "....",
                ".#..",
                "0.0.",
                "....");
            Settle(board, out var result);

            var seen = new bool[board.Rows * board.Cols];
            foreach (int to in result.MoveTo)
            {
                Assert.IsFalse(seen[to], "two blocks landed on the same cell");
                seen[to] = true;
            }
        }

        [Test]
        public void BoxesNeverMove()
        {
            var board = BoardBuilder.Parse(
                "00",
                "#.",
                "..");
            Settle(board, out var result);

            Assert.IsTrue(board.Cells[board.Index(1, 0)].IsBox, "the Box stayed on its row");
            foreach (int from in result.MoveFrom)
                Assert.AreNotEqual(board.Index(1, 0), from, "no move originated from the Box's cell");
        }
    }
}