using System;
using BlastGame.Core;
using NUnit.Framework;

namespace BlastGame.Tests
{
    /// <summary>
    /// Icon tiers at their boundary values.
    /// </summary>
    /// <remarks>
    /// Worth more than its size for two reasons. Off-by-one is the classic mistake in threshold code,
    /// and the case document contradicts itself here - Example 1 lists C=9 and then says "more than
    /// 10", while Example 2 is consistent. These cases pin down the reading chosen (strictly greater),
    /// so a later "fix" cannot quietly change it.
    /// </remarks>
    [TestFixture]
    public class IconTierTests
    {
        private const int A = 4, B = 7, C = 9;   // Example 1

        /// <summary>A single horizontal run of one colour, so the group size is exactly the width.</summary>
        private static int TierOfGroupOfSize(int size, int a = A, int b = B, int c = C)
        {
            var config = new BoardConfig(1, size, 2, a, b, c, 0);
            var cells = new Cell[size];
            for (int i = 0; i < size; i++) cells[i] = Cell.MakeColor(0);

            var groups = new GroupFinder(config);
            groups.Recalculate(cells);
            return groups.TierAt(0);
        }

        [TestCase(2, 0, TestName = "size 2 shows the default icon")]
        [TestCase(A, 0, TestName = "size A stays on the default icon")]
        [TestCase(A + 1, 1, TestName = "size A+1 reaches the first icon")]
        [TestCase(B, 1, TestName = "size B stays on the first icon")]
        [TestCase(B + 1, 2, TestName = "size B+1 reaches the second icon")]
        [TestCase(C, 2, TestName = "size C stays on the second icon")]
        [TestCase(C + 1, 3, TestName = "size C+1 reaches the third icon")]
        [TestCase(40, 3, TestName = "far above C stays on the third icon")]
        public void TierAt_Boundaries(int groupSize, int expectedTier)
        {
            Assert.AreEqual(expectedTier, TierOfGroupOfSize(groupSize));
        }

        [Test]
        public void Example2Thresholds_MatchTheDocument()
        {
            // M=5 N=8 K=4 A=4 B=6 C=8 - the internally consistent example.
            Assert.AreEqual(0, TierOfGroupOfSize(4, 4, 6, 8), "4 is not more than 4");
            Assert.AreEqual(1, TierOfGroupOfSize(6, 4, 6, 8));
            Assert.AreEqual(2, TierOfGroupOfSize(7, 4, 6, 8));
            Assert.AreEqual(2, TierOfGroupOfSize(8, 4, 6, 8), "8 is not more than 8");
            Assert.AreEqual(3, TierOfGroupOfSize(9, 4, 6, 8));
        }

        [Test]
        public void BoxAndEmptyCells_ReportTheDefaultTier()
        {
            var board = BoardBuilder.Parse(
                "000",
                "0#0",
                ".00");

            var groups = board.Analyze(A, B, C);

            Assert.AreEqual(0, groups.TierAt(board.Index(1, 1)), "a Box has no group and so no tier");
            Assert.AreEqual(0, groups.TierAt(board.Index(0, 0)), "an empty cell has no group and so no tier");
            Assert.AreEqual(1, groups.TierAt(board.Index(2, 0)), "the seven remaining cells are one group");
        }

        [Test]
        public void TierFollowsTheGroup_NotTheCell()
        {
            // Same colour, two components: only the big one is promoted. If tiers were computed from
            // anything other than the containing group, both runs would show the same icon.
            var board = BoardBuilder.Parse(
                "00000#00");

            var groups = board.Analyze(A, B, C);

            Assert.AreEqual(1, groups.TierAt(board.Index(0, 0)), "the run of five is above A");
            Assert.AreEqual(0, groups.TierAt(board.Index(0, 6)), "the run of two is not");
        }

        [Test]
        public void NonAscendingThresholds_AreRejected()
        {
            // With A=7, B=4 a group of 8 would match "> B" first and the first icon could never appear.
            // LevelConfig quietly repairs this for the designer; Core refuses it for the programmer.
            Assert.Throws<ArgumentException>(() => new BoardConfig(5, 5, 6, 7, 4, 9, 0).Validate());
            Assert.Throws<ArgumentException>(() => new BoardConfig(5, 5, 6, 4, 4, 9, 0).Validate());
            Assert.Throws<ArgumentException>(() => new BoardConfig(5, 5, 6, 4, 7, 7, 0).Validate());
        }
    }
}