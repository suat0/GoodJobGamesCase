using System;
using System.Diagnostics;

namespace BlastGame.Core
{
    // Rearranges the colours so at least one blastable group exists, in a single pass: shuffle once
    // for the look of it, then place one group deliberately. Blind shuffle-and-recheck has no bound on
    // how long it runs, which the case document rules out by name.
    //
    // Only the Color field is ever written. Boxes, holes and Box health stay where they are, which is
    // why the remedy is a shuffle and not a regenerated board - a player who has cracked half the
    // Boxes must not watch them move and heal.
    //
    // A group needs somewhere to go (two adjacent coloured cells) and something to make it from (a
    // repeated colour). Only the first can be missing without remedy.
    public sealed class DeadlockResolver
    {
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

        // False only when no two coloured cells are adjacent - a colour cannot make two cells
        // neighbours. Exact rather than a guess, and unreachable on a generated board, whose top row
        // never holds a Box and is therefore always a full run of colours.
        public bool TryResolve(Span<Cell> cells)
        {
            AssertMatchesBoard(cells);

            if (!Survey(cells, out int slotCount, out int pairLeft, out int pairRight, out int bestColorCount))
                return false;

            Shuffle(cells, slotCount);

            // Tier one wherever it applies: it preserves every colour count, so the board reads as the
            // same pieces rearranged rather than a new hand dealt.
            if (bestColorCount >= GroupFinder.MinBlastableSize)
                ForceGroup(cells, slotCount, pairLeft, pairRight);
            else
                AssignColorToPair(cells, pairLeft, pairRight);

            AssertGroupExists(cells, pairLeft, pairRight);
            return true;
        }

        // For a board where no colour occurs twice - three cells holding three different colours, which
        // small boards produce routinely. Rearranging a set with no repeat still has no repeat, so
        // swapping cannot help; writing can, and one cell is the smallest deviation available.
        private static void AssignColorToPair(Span<Cell> cells, int pairLeft, int pairRight)
        {
            cells[pairRight].Color = cells[pairLeft].Color;   // in place, never through a local copy
        }

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

        // Reservoir sampling, k=1. Taking the first pair would put the group in the same corner every
        // time; collecting all pairs would allocate a list of unknown size.
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

        // Fisher-Yates: the random index comes only from the part not yet fixed. Drawing from the whole
        // range is biased - n^n paths do not divide evenly into n! permutations.
        private void Shuffle(Span<Cell> cells, int slotCount)
        {
            for (int i = slotCount - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                SwapColors(cells, slots[i], slots[j]);
            }
        }

        private void ForceGroup(Span<Cell> cells, int slotCount, int pairLeft, int pairRight)
        {
            byte color = MostCommonColor(cells, slotCount);

            PlaceColor(cells, slotCount, pairLeft, pairRight, color);
            PlaceColor(cells, slotCount, pairRight, pairLeft, color);
        }

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

        private void ClearCounts(ReadOnlySpan<Cell> cells, int slotCount)
        {
            for (int s = 0; s < slotCount; s++) colorCounts[cells[slots[s]].Color] = 0;
        }

        private static void SwapColors(Span<Cell> cells, int a, int b)
        {
            byte held = cells[a].Color;
            cells[a].Color = cells[b].Color;   // in place; see the Cell copy trap
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
