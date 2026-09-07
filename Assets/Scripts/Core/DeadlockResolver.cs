using System;
using System.Diagnostics;

namespace BlastGame.Core
{
    // Rearranges the colours on the board so at least one blastable group exists, in a single pass:
    // shuffle once for the look of it, then PLACE one group deliberately. Blind shuffle-and-recheck
    // has no bound on how long it runs, which the case document rules out by name.
    //
    // Only the Color field is ever written: Boxes, holes and Box health stay exactly where they are.
    // That is the line this class does not cross, and it is why the remedy is a shuffle rather than a
    // regenerated board - a player who has cracked half the Boxes must not watch them move and heal.
    //
    // Making a group needs two things, and they fail for different reasons: somewhere to PUT it (two
    // adjacent coloured cells) and something to BUILD it from (a colour that occurs twice). The first
    // is the only one that can be missing without remedy - see TryResolve.
    public sealed class DeadlockResolver
    {
        // The whole byte domain, not ColorCount: a hand-authored board using a colour outside the
        // configured palette would otherwise index out of range, to save 1 KB.
        private const int ColorDomainSize = 256;

        private readonly int rows;
        private readonly int cols;
        private readonly Random rng;

        private readonly int[] slots;         // indices of the coloured cells, refilled every call
        private readonly int[] colorCounts;

        public DeadlockResolver(BoardConfig config, Random rng)
        {
            config.Validate();

            rows = config.Rows;
            cols = config.Cols;

            this.rng = rng ?? throw new ArgumentNullException(nameof(rng));

            slots = new int[rows * cols];
            colorCounts = new int[ColorDomainSize];
        }

        // False only when no two coloured cells are adjacent, which no amount of recolouring can fix:
        // a colour cannot make two cells neighbours. That is exact, not a guess, and it cannot happen
        // on a generated board - the top row never holds a Box, so it is always a full run of colours.
        public bool TryResolve(Span<Cell> cells)
        {
            AssertMatchesBoard(cells);

            if (!Survey(cells, out int slotCount, out int pairLeft, out int pairRight, out int bestColorCount))
                return false;

            Shuffle(cells, slotCount);

            // Two tiers, and the first is preferred wherever it applies: it preserves every colour
            // count, so the board reads as the same pieces rearranged rather than a new hand dealt.
            if (bestColorCount >= GroupFinder.MinBlastableSize)
                ForceGroup(cells, slotCount, pairLeft, pairRight);
            else
                AssignColorToPair(cells, pairLeft, pairRight);

            AssertGroupExists(cells, pairLeft, pairRight);
            return true;
        }

        // The fallback, for a board where no colour occurs twice - three cells holding three different
        // colours, which small boards produce routinely. Swapping cannot help there: rearranging a set
        // with no repeat still has no repeat, whatever order it is put in.
        //
        // Writing can, and one cell is the smallest deviation available. It breaks the "a shuffle
        // rearranges, it does not reissue" rule the tier above keeps - deliberately, only here, and by
        // exactly one cell. The alternative is a level with no move and no remedy.
        private static void AssignColorToPair(Span<Cell> cells, int pairLeft, int pairRight)
        {
            cells[pairRight].Color = cells[pairLeft].Color;   // in place, never through a local copy
        }

        // One walk gives everything the rest needs: the coloured cells, the colour histogram, and one
        // adjacent pair. Single pass because the feasibility check IS the pair the guarantee step uses.
        // Returns whether a group can be PLACED; bestColorCount says whether one can be BUILT by
        // swapping, which is what picks the tier. Reported apart because they are different failures.
        private bool Survey(ReadOnlySpan<Cell> cells, out int slotCount, out int pairLeft, out int pairRight,
                            out int bestColorCount)
        {
            slotCount = 0;
            pairLeft = -1;
            pairRight = -1;
            bestColorCount = 0;

            int pairCount = 0;

            for (int i = 0; i < cells.Length; i++)
            {
                if (!cells[i].IsColor) continue;

                slots[slotCount++] = i;

                int seen = ++colorCounts[cells[i].Color];
                if (seen > bestColorCount) bestColorCount = seen;

                // Up and right only, so each orthogonal pair reaches the sampler exactly once.
                TrySampleNeighborPair(cells, i, Grid.Up, ref pairCount, ref pairLeft, ref pairRight);
                TrySampleNeighborPair(cells, i, Grid.Right, ref pairCount, ref pairLeft, ref pairRight);
            }

            ClearCounts(cells, slotCount);

            // Only the placement question. Whether a colour occurs twice decides which tier runs, not
            // whether the resolver can run at all.
            return pairCount > 0;
        }

        // Reservoir sampling, k=1. Taking the first pair would put the forced group in the same corner
        // every time; collecting all pairs would allocate a list of unknown size.
        private void TrySampleNeighborPair(ReadOnlySpan<Cell> cells, int index, int direction,
                                           ref int pairCount, ref int pairLeft, ref int pairRight)
        {
            if (!Grid.TryStep(index, direction, rows, cols, out int neighbor)) return;
            if (!cells[neighbor].IsColor) return;

            pairCount++;
            if (rng.Next(pairCount) != 0) return;

            pairLeft = index;
            pairRight = neighbor;
        }

        // Fisher-Yates: the random index is drawn only from the part not yet fixed. Drawing from the
        // whole range every time is biased - n^n paths do not divide evenly into n! permutations.
        private void Shuffle(Span<Cell> cells, int slotCount)
        {
            for (int i = slotCount - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                SwapColors(cells, slots[i], slots[j]);
            }
        }

        // Puts the most common colour on both cells of the sampled pair - this is what makes one pass
        // enough. Unconditional on purpose: one code path, so "finishes in one pass" is a property of
        // the code rather than a claim about the common case.
        private void ForceGroup(Span<Cell> cells, int slotCount, int pairLeft, int pairRight)
        {
            byte color = MostCommonColor(cells, slotCount);

            PlaceColor(cells, slotCount, pairLeft, pairRight, color);
            PlaceColor(cells, slotCount, pairRight, pairLeft, color);
        }

        // Donor is any cell other than the target and its partner. Cannot fail: the colour occurs at
        // least twice and at most one of the pair already holds an occurrence.
        private void PlaceColor(Span<Cell> cells, int slotCount, int target, int partner, byte color)
        {
            if (cells[target].Color == color) return;

            for (int s = 0; s < slotCount; s++)
            {
                int donor = slots[s];
                if (donor == target || donor == partner) continue;
                if (cells[donor].Color != color) continue;

                SwapColors(cells, target, donor);
                return;
            }

            throw new InvalidOperationException(
                $"No donor cell found for colour {color}, which Survey reported as occurring at least twice.");
        }

        private byte MostCommonColor(ReadOnlySpan<Cell> cells, int slotCount)
        {
            // Recounted rather than carried over from Survey: the totals are unchanged, but a count
            // that is rebuilt cannot be a count that went stale.
            for (int s = 0; s < slotCount; s++) colorCounts[cells[slots[s]].Color]++;

            byte best = 0;
            int bestCount = -1;

            for (int s = 0; s < slotCount; s++)
            {
                byte color = cells[slots[s]].Color;
                if (colorCounts[color] <= bestCount) continue;

                best = color;
                bestCount = colorCounts[color];
            }

            ClearCounts(cells, slotCount);
            return best;
        }

        // Only the entries this pass touched. Array.Clear over all 256 would do more work than the
        // board has cells.
        private void ClearCounts(ReadOnlySpan<Cell> cells, int slotCount)
        {
            for (int s = 0; s < slotCount; s++) colorCounts[cells[slots[s]].Color] = 0;
        }

        private static void SwapColors(Span<Cell> cells, int a, int b)
        {
            byte held = cells[a].Color;
            cells[a].Color = cells[b].Color;   // in place, never through a local copy
            cells[b].Color = held;
        }

        [Conditional("UNITY_ASSERTIONS")]
        private void AssertMatchesBoard(ReadOnlySpan<Cell> cells)
        {
            if (cells.Length != slots.Length)
                throw new ArgumentException(
                    $"Board has {cells.Length} cells but this DeadlockResolver was built for {rows}x{cols}.",
                    nameof(cells));
        }

        // The two sampled cells are adjacent by construction, so equal colours there is a group.
        [Conditional("UNITY_ASSERTIONS")]
        private static void AssertGroupExists(ReadOnlySpan<Cell> cells, int pairLeft, int pairRight)
        {
            if (cells[pairLeft].Color != cells[pairRight].Color)
                throw new InvalidOperationException(
                    $"Shuffle finished without a group: cells {pairLeft} and {pairRight} hold " +
                    $"{cells[pairLeft].Color} and {cells[pairRight].Color}.");
        }
    }
}
