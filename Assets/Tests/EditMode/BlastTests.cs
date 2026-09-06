using System;
using BlastGame.Core;
using NUnit.Framework;

namespace BlastGame.Tests
{
    [TestFixture]
    public class BlastTests
    {
        private static string Render(Board b)
        {
            var sb = new System.Text.StringBuilder();
            for (int row = b.Rows - 1; row >= 0; row--)
            {
                for (int col = 0; col < b.Cols; col++)
                {
                    Cell c = b.CellAt(row, col);
                    sb.Append(c.IsBox ? (char)('0' + c.Health + 10) : c.IsEmpty ? '.' : (char)('0' + c.Color));
                }
                if (row > 0) sb.Append('/');
            }
            return sb.ToString();
        }

        // --- Test 2: a Box takes one damage per blast, not per adjacent block ---------------------

        [Test]
        public void BoxTakesExactlyOneDamage_HoweverManyGroupMembersTouchIt()
        {
            //  1 1 1     five 1s form one group, and three of them sit next to the Box
            //  1 # .
            //  1 . .
            var board = BoardBuilder.Parse(
                "111",
                "1#.",
                "1..").ToBoard(colorCount: 1);

            int boxIndex = 1 * 3 + 1;
            Assert.AreEqual(Cell.BoxMaxHealth, board.CellAt(boxIndex).Health, "starts at full health");

            Assert.IsTrue(board.TryBlast(0));

            Assert.AreEqual(1, board.LastBlast.DamagedBoxes.Length, "one Box was hit");
            Assert.AreEqual(0, board.LastBlast.BrokenBoxes.Length, "and it survived");
            Assert.AreEqual(Cell.BoxMaxHealth - 1, board.CellAt(boxIndex).Health,
                "three adjacent group members must still deal exactly one damage");
        }

        [Test]
        public void TwoSeparateBoxesEachTakeOneDamage()
        {
            //  # 1 #
            //  . 1 .
            //  . . .     the top row must never hold a Box - the refill rule depends on it
            //  # 1 #
            //  . 1 .
            var board = BoardBuilder.Parse(
                "...",
                "#1#",
                ".1.").ToBoard(colorCount: 1);

            Assert.IsTrue(board.TryBlast(1));
            Assert.AreEqual(2, board.LastBlast.DamagedBoxes.Length, "each Box is stamped independently");
        }

        [Test]
        public void BoxDirectlyAboveTheGroupIsDamaged()
        {
            //  . .
            //  # .
            //  1 1
            var board = BoardBuilder.Parse(
                "..",
                "#.",
                "11").ToBoard(colorCount: 1);

            int boxIndex = 1 * 2 + 0;
            Assert.IsTrue(board.TryBlast(0));
            Assert.AreEqual(Cell.BoxMaxHealth - 1, board.CellAt(boxIndex).Health);
        }

        [Test]
        public void OnlyOrthogonalNeighboursCount()
        {
            //  . . #      the Box touches nothing in the group except diagonally
            //  1 1 .
            //  . . .
            //  . . #      the Box only touches the group corner to corner
            //  1 1 .
            var board = BoardBuilder.Parse(
                "...",
                "..#",
                "11.").ToBoard(colorCount: 1);

            int boxIndex = 1 * 3 + 2;
            Assert.IsTrue(board.TryBlast(0));
            Assert.AreEqual(Cell.BoxMaxHealth, board.CellAt(boxIndex).Health, "a diagonal touch deals nothing");
        }

        [Test]
        public void BoxBreaksOnTheSecondBlast_AndItsCellRefillsInTheSameMove()
        {
            var board = BoardBuilder.Parse(
                "...",
                "1#1",
                "111").ToBoard(colorCount: 1);

            int boxIndex = 1 * 3 + 1;

            Assert.IsTrue(board.TryBlast(0));
            Assert.AreEqual(1, board.CellAt(boxIndex).Health, "first blast damages");

            // The board refilled with colour 0 everywhere; blast again next to the Box.
            Assert.IsTrue(board.TryBlast(0));
            Assert.AreEqual(1, board.LastBlast.BrokenBoxes.Length, "second blast breaks it");
            Assert.IsFalse(board.CellAt(boxIndex).IsBox, "the Box is gone");
            Assert.IsFalse(board.CellAt(boxIndex).IsEmpty,
                "and gravity filled its cell within the same move, per Karar 24");
        }

        [Test]
        public void RemainingBoxesDropsWhenABoxBreaks()
        {
            var board = BoardBuilder.Parse(
                "...",
                "1#1",
                "111").ToBoard(colorCount: 1);

            Assert.AreEqual(1, board.RemainingBoxes());
            board.TryBlast(0);
            board.TryBlast(0);
            Assert.AreEqual(0, board.RemainingBoxes());
        }

        // --- what counts as a legal tap -----------------------------------------------------------

        [Test]
        public void TappingSomethingUnblastable_ChangesNothing()
        {
            var board = BoardBuilder.Parse(
                "01",
                "2#").ToBoard();

            string before = Render(board);

            Assert.IsFalse(board.TryBlast(board.Index(1, 0)), "a lone coloured cell");
            Assert.IsFalse(board.TryBlast(board.Index(0, 1)), "a Box");
            Assert.IsFalse(board.TryBlast(-1), "an index off the board");
            Assert.IsFalse(board.TryBlast(4), "an index past the end");

            Assert.AreEqual(before, Render(board), "a rejected tap must leave the board untouched");
        }

        [Test]
        public void TappingAnEmptyCellIsRejected()
        {
            var board = BoardBuilder.Parse(
                "11",
                ".1").ToBoard(colorCount: 1);

            Assert.IsFalse(board.TryBlast(board.Index(0, 0)));
        }

        // --- result bookkeeping -------------------------------------------------------------------

        [Test]
        public void ResultDescribesTheWholeMove()
        {
            var board = BoardBuilder.Parse(
                "111",
                "111").ToBoard(colorCount: 1);

            Assert.IsTrue(board.TryBlast(0));

            Assert.AreEqual(0, board.LastBlast.TappedIndex);
            Assert.AreEqual(6, board.LastBlast.BlastedGroupSize);
            Assert.AreEqual(6, board.LastBlast.Removed.Length, "every group member is reported removed");
            Assert.AreEqual(6, board.LastBlast.MoveTo.Length, "six new blocks entered");

            foreach (int from in board.LastBlast.MoveFrom)
                Assert.IsTrue(board.LastBlast.IsSpawn(from), "the board was cleared, so all moves are spawns");
        }

        [Test]
        public void GroupDataIsFreshTheInstantBlastReturns()
        {
            var board = BoardBuilder.Parse(
                "12",
                "11").ToBoard(colorCount: 1);

            board.TryBlast(0);

            // The three 1s went; the lone 2 fell to the bottom of its column and the rest refilled with
            // colour 0, leaving one group of three. Stale group data would still report the old sizes.
            Assert.AreEqual(3, board.GroupSizeAt(board.Index(1, 0)),
                "groups were rebuilt before the call returned");
            Assert.AreEqual(3, board.LargestGroupSize);
            Assert.AreEqual(1, board.GroupSizeAt(board.Index(0, 1)), "the surviving 2 is alone");
        }

        [Test]
        public void SuccessiveBlastsDoNotLeakDamageAcrossMoves()
        {
            // The stamp counter is what stops an old mark being read as current. If Clear-by-stamp were
            // broken, the Box would refuse a second hit and never break.
            var board = BoardBuilder.Parse(
                "...",
                "1#1",
                "111").ToBoard(colorCount: 1);

            board.TryBlast(0);
            Assert.AreEqual(1, board.LastBlast.DamagedBoxes.Length);

            board.TryBlast(0);
            Assert.AreEqual(1, board.LastBlast.BrokenBoxes.Length, "the second blast registered");
        }
    }
}