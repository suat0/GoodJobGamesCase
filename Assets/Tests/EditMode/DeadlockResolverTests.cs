using BlastGame.Core;
using NUnit.Framework;

namespace BlastGame.Tests
{
    /// <summary>
    /// The two guarantees the resolver is built on - that a shuffle always produces a playable board,
    /// and that it rearranges rather than reissues - plus the two properties that turned out to be
    /// testable once it was written: that detection is right, and that the forced group does not
    /// always land in the same place.
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

        // The tier-1 property: where a colour already occurs twice the resolver swaps, so nothing is
        // reissued. The tier-2 fallback below is the deliberate exception.
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

        // --- Tier 2: assigning, for boards that cannot be swapped into a group --------------------

        [Test]
        public void Shuffle_AssignsAColour_WhenNoneOccursTwice()
        {
            // Four adjacent cells, four different colours. Swapping cannot help - rearranging a set
            // with no repeat still has no repeat - but there is somewhere to put a group, so the
            // resolver writes one rather than giving up. Small boards produce this routinely.
            var board = BoardBuilder.Parse(
                "01",
                "23").ToBoard(colorCount: 4, seed: 1);

            Assert.IsTrue(board.IsDeadlocked);
            Assert.IsTrue(board.TryResolveDeadlock(),
                "two adjacent coloured cells means a group can be placed, whatever the colours are");
            Assert.IsFalse(board.IsDeadlocked, $"resolved but still no move\n{board}");
        }

        [Test]
        public void Shuffle_ChangesExactlyOneCell_WhenItHasToAssign()
        {
            var board = BoardBuilder.Parse(
                "01",
                "23").ToBoard(colorCount: 4, seed: 1);

            int[] before = CountColors(board);
            board.TryResolveDeadlock();
            int[] after = CountColors(board);

            // One cell took a neighbour's colour: one count down by one, one up by one. Anything more
            // would be a reissued board rather than a nudged one.
            int lost = 0, gained = 0;
            for (int color = 0; color < before.Length; color++)
            {
                int delta = after[color] - before[color];
                if (delta < 0) lost -= delta;
                if (delta > 0) gained += delta;
            }

            Assert.AreEqual(1, lost, $"more than one cell changed colour\n{board}");
            Assert.AreEqual(1, gained, $"more than one cell changed colour\n{board}");
        }

        [Test]
        public void Shuffle_SucceedsWheneverTwoColouredCellsAreAdjacent_AcrossManyBoards()
        {
            // Random small boards with no Boxes and no holes, so a pair of adjacent coloured cells
            // always exists. What decides success is placement, never whether a swap was available.
            var rng = new System.Random(4242);

            for (int trial = 0; trial < SeedCount; trial++)
            {
                int rows = 2 + rng.Next(3);
                int cols = 2 + rng.Next(3);

                var cells = new Cell[rows * cols];
                for (int i = 0; i < cells.Length; i++) cells[i] = Cell.MakeColor((byte)rng.Next(6));

                var config = new BoardConfig(rows, cols, 6, 1, 2, 3, boxCount: 0);
                var board = new Board(config, new System.Random(trial));
                board.LoadState(cells);

                if (!board.IsDeadlocked) continue;

                Assert.IsTrue(board.TryResolveDeadlock(), $"trial {trial}: nothing to place a group on?\n{board}");
                Assert.IsFalse(board.IsDeadlocked, $"trial {trial}: resolved but still no move\n{board}");
            }
        }

        // --- The one condition nothing can fix ----------------------------------------------------

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

        // --- Generation is where the guarantee has to start ---------------------------------------

        [Test]
        public void GeneratedBoards_AlwaysHaveALegalMove_AcrossManySeedsAndShapes()
        {
            // Small boards with many colours are where this can actually fail, and they are the shapes
            // nobody plays by accident. A 2x2 holding one Box has three coloured cells; with six
            // colours all three come out different more often than not, and the shuffle cannot help
            // because it swaps and therefore needs a colour that already occurs twice.
            var shapes = new[]
            {
                new { Rows = 2,  Cols = 2,  Colors = 6, Boxes = 1 },
                new { Rows = 2,  Cols = 2,  Colors = 6, Boxes = 0 },
                new { Rows = 2,  Cols = 3,  Colors = 6, Boxes = 1 },
                new { Rows = 3,  Cols = 3,  Colors = 6, Boxes = 2 },
                new { Rows = 4,  Cols = 10, Colors = 5, Boxes = 6 },

                // No Boxes at all - the shape both of the case document's examples have, and the one
                // that used to skip the group scan entirely.
                new { Rows = 8,  Cols = 8,  Colors = 4, Boxes = 0 },

                new { Rows = 10, Cols = 10, Colors = 6, Boxes = 8 },
            };

            foreach (var shape in shapes)
            {
                for (int seed = 1; seed <= SeedCount; seed++)
                {
                    var config = new BoardConfig(shape.Rows, shape.Cols, shape.Colors, 1, 2, 3, shape.Boxes);
                    var board = new Board(config, new System.Random(seed));

                    board.Generate();

                    Assert.IsFalse(board.IsDeadlocked,
                        $"{shape.Rows}x{shape.Cols} K={shape.Colors} Box={shape.Boxes} seed {seed}: " +
                        $"generated with no legal move\n{board}");
                }
            }
        }

        [Test]
        public void GeneratedBoards_HaveTheirGroupsScanned_EvenWithNoBoxes()
        {
            // A board whose groups were never scanned reports every cell unblastable and the whole
            // board deadlocked, which is indistinguishable from a genuinely dead board.
            var config = new BoardConfig(8, 8, 4, 4, 7, 9, boxCount: 0);
            var board = new Board(config, new System.Random(1));

            board.Generate();

            bool anyBlastable = false;
            for (int i = 0; i < board.CellCount; i++)
                if (board.IsBlastable(i)) { anyBlastable = true; break; }

            Assert.IsTrue(anyBlastable, $"64 cells over four colours and nothing can be tapped\n{board}");
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