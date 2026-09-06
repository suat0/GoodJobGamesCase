using BlastGame.Core;
using NUnit.Framework;

namespace BlastGame.Tests
{
    /// <summary>
    /// Tests 6 and 7 from the plan in DECISIONS.md, plus the two properties that turned out to be
    /// testable once the resolver was written: that detection is right, and that the forced group does
    /// not always land in the same place.
    /// </summary>
    /// <remarks>
    /// Most of these run over 200 seeds rather than one. A guarantee that holds for seed 42 is not a
    /// guarantee - it is an anecdote, and the whole claim of this phase is that the shuffle always
    /// succeeds in one pass.
    /// </remarks>
    [TestFixture]
    public class DeadlockResolverTests
    {
        private const int SeedCount = 200;

        // A board with no two adjacent cells of the same colour. Every colour appears often enough to
        // form a group, so the deadlock is a matter of arrangement - which is exactly what shuffle fixes.
        private static readonly string[] LatinSquare =
        {
            "0123",
            "1230",
            "2301",
            "3012"
        };

        // The same idea with Boxes and a hole under one of them, so the resolver has to cope with a board
        // that is not a clean rectangle of colours.
        private static readonly string[] DeadlockedWithBoxes =
        {
            "0120",
            "1#01",
            "2.12",
            "0121"
        };

        // --- Test 6: the shuffle always produces a playable board ---------------------------------

        [Test]
        public void Shuffle_AlwaysProducesABlastableGroup_AcrossManySeeds()
        {
            for (int seed = 0; seed < SeedCount; seed++)
            {
                var board = BoardBuilder.Parse(LatinSquare).ToBoard(colorCount: 4, seed: seed);

                Assert.IsTrue(board.IsDeadlocked, $"seed {seed}: the starting board is meant to be deadlocked");
                Assert.IsTrue(board.TryResolveDeadlock(), $"seed {seed}: shuffle reported failure");
                Assert.IsFalse(board.IsDeadlocked, $"seed {seed}: still no move after shuffling\n{board}");
            }
        }

        [Test]
        public void Shuffle_WorksOnABoardWithBoxesAndHoles_AcrossManySeeds()
        {
            for (int seed = 0; seed < SeedCount; seed++)
            {
                var board = BoardBuilder.Parse(DeadlockedWithBoxes).ToBoard(colorCount: 3, seed: seed);

                Assert.IsTrue(board.TryResolveDeadlock(), $"seed {seed}: shuffle reported failure");
                Assert.IsFalse(board.IsDeadlocked, $"seed {seed}: still no move after shuffling\n{board}");
            }
        }

        // --- Test 7: a shuffle rearranges, it does not reissue ------------------------------------

        [Test]
        public void Shuffle_PreservesColourCounts_AcrossManySeeds()
        {
            for (int seed = 0; seed < SeedCount; seed++)
            {
                var board = BoardBuilder.Parse(DeadlockedWithBoxes).ToBoard(colorCount: 3, seed: seed);

                int[] before = CountColors(board);
                board.TryResolveDeadlock();
                int[] after = CountColors(board);

                for (int color = 0; color < before.Length; color++)
                    Assert.AreEqual(before[color], after[color],
                        $"seed {seed}: colour {color} went from {before[color]} to {after[color]}\n{board}");
            }
        }

        [Test]
        public void Shuffle_LeavesBoxesAndHolesWhereTheyWere()
        {
            var board = BoardBuilder.Parse(DeadlockedWithBoxes).ToBoard(colorCount: 3, seed: 7);

            board.TryResolveDeadlock();

            // Boxes do not fall and do not move; only the colours of coloured cells are permuted.
            Assert.AreEqual(CellType.Box, board.CellAt(2, 1).Type, "the Box left its cell");
            Assert.AreEqual(Cell.BoxMaxHealth, board.CellAt(2, 1).Health, "the Box changed health");
            Assert.AreEqual(CellType.Empty, board.CellAt(1, 1).Type, "the hole under the Box was filled");
        }

        // --- Unsolvable boards are a condition, not a guess ---------------------------------------

        [Test]
        public void Shuffle_Fails_WhenNoColourOccursTwice()
        {
            // Four adjacent cells, four different colours: there is nowhere to build a pair from.
            var board = BoardBuilder.Parse(
                "01",
                "23").ToBoard(colorCount: 4, seed: 1);

            Assert.IsTrue(board.IsDeadlocked);
            Assert.IsFalse(board.TryResolveDeadlock(), "no colour occurs twice, so no arrangement has a group");
        }

        [Test]
        public void Shuffle_Fails_WhenNoTwoColouredCellsAreAdjacent()
        {
            // Plenty of repeated colour, but every coloured cell is isolated - a pair has nowhere to sit.
            var board = BoardBuilder.Parse(
                "0.0",
                "...",
                "0.0").ToBoard(colorCount: 1, seed: 1);

            Assert.IsTrue(board.IsDeadlocked);
            Assert.IsFalse(board.TryResolveDeadlock(), "isolated cells cannot be made adjacent by recolouring");
        }

        // --- Detection ----------------------------------------------------------------------------

        [Test]
        public void IsDeadlocked_IsFalse_WhenAnyPairExists()
        {
            var deadlocked = BoardBuilder.Parse(LatinSquare).ToBoard(colorCount: 4);
            Assert.IsTrue(deadlocked.IsDeadlocked);

            var playable = BoardBuilder.Parse(
                "0123",
                "1230",
                "2301",
                "3011").ToBoard(colorCount: 4);   // one changed cell, one pair
            Assert.IsFalse(playable.IsDeadlocked, "a single pair is enough to be playable");
        }

        // --- Where the forced group lands ---------------------------------------------------------

        [Test]
        public void ForcedGroup_DoesNotAlwaysLandInTheSamePlace()
        {
            // Two isolated candidate pairs and colours {0, 0, 1, 2}. The forced pair gets the doubled
            // colour, so the other pair is left holding 1 and 2 and cannot become a group by accident:
            // wherever the group appears is where the resolver chose to put it.
            bool topPairChosen = false;
            bool bottomPairChosen = false;

            for (int seed = 0; seed < SeedCount; seed++)
            {
                var board = BoardBuilder.Parse(
                    "01..",
                    "....",
                    "..02").ToBoard(colorCount: 3, seed: seed);

                Assert.IsTrue(board.TryResolveDeadlock(), $"seed {seed}: shuffle reported failure");

                if (board.IsBlastable(board.Index(2, 0))) topPairChosen = true;
                if (board.IsBlastable(board.Index(0, 2))) bottomPairChosen = true;
            }

            Assert.IsTrue(topPairChosen && bottomPairChosen,
                "over 200 seeds the group only ever appeared on one of the two pairs, so the pair is not "
                + "being sampled - it is being taken in scan order");
        }

        // --- Seeded randomness stays seeded --------------------------------------------------------

        [Test]
        public void SameSeed_ProducesTheSameShuffle()
        {
            var first = BoardBuilder.Parse(LatinSquare).ToBoard(colorCount: 4, seed: 99);
            var second = BoardBuilder.Parse(LatinSquare).ToBoard(colorCount: 4, seed: 99);

            first.TryResolveDeadlock();
            second.TryResolveDeadlock();

            Assert.AreEqual(first.ToString(), second.ToString(), "an injected seed has to replay exactly");
        }

        private static int[] CountColors(Board board)
        {
            var counts = new int[256];

            for (int i = 0; i < board.CellCount; i++)
            {
                Cell cell = board.CellAt(i);
                if (cell.IsColor) counts[cell.Color]++;
            }

            return counts;
        }
    }
}