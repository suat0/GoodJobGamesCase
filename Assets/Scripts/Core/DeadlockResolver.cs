using System;
using System.Diagnostics;

namespace BlastGame.Core
{
    /// <summary>
    /// Rearranges the colours already on the board so that at least one blastable group exists,
    /// in a single pass with no retries.
    /// </summary>
    /// <remarks>
    /// The case document rules out the obvious implementation by name: shuffling blindly and re-checking
    /// until something works has no bound on how long it runs, and on a board with only one valid
    /// arrangement it may never find it. This does the opposite - it shuffles once for the look of it,
    /// then <i>places</i> one group deliberately, so the method returns after exactly one pass.
    /// <para>
    /// Shaped like <see cref="GroupFinder"/> and <see cref="GravityResolver"/>: scratch arrays are owned
    /// here and allocated once, the board arrives per call as a span and is never retained (Karar 13).
    /// The span is mutable because this writes; <see cref="Random"/> is injected so a seeded run replays.
    /// </para>
    /// <para>
    /// <b>Colours move, cells do not.</b> Only the <c>Color</c> field of coloured cells is permuted -
    /// Boxes, their health, and empty cells are left exactly where they are. A shuffle is a rearrangement
    /// of what the player already has, which is also why the guarantee step swaps rather than overwrites:
    /// overwriting would quietly delete colours and the fifteen reds a player was saving could come back
    /// as three.
    /// </para>
    /// </remarks>
    public sealed class DeadlockResolver
    {
        /// <summary>
        /// Colour is a <c>byte</c>, so this is the whole domain. Sizing the histogram by
        /// <c>ColorCount</c> instead would index out of range on a hand-authored board that uses a
        /// colour outside the configured palette - a crash in the defensive path, to save 1 KB.
        /// </summary>
        private const int ColorDomainSize = 256;

        private readonly int rows;
        private readonly int cols;
        private readonly Random rng;

        /// <summary>Indices of the coloured cells, refilled by every call. Boxes and empties never enter.</summary>
        private readonly int[] slots;

        /// <summary>How often each colour occurs. Only the entries touched this pass are read.</summary>
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

        /// <summary>
        /// Rearranges the board so at least one group of two adjacent same-coloured cells exists.
        /// </summary>
        /// <returns>
        /// False when no rearrangement can produce a group. That is not a guess: it happens exactly when
        /// no colour occurs twice, or when no two coloured cells are adjacent - in either case there is
        /// nowhere to put a pair. The level is lost (DECISIONS.md, Karar 8).
        /// </returns>
        public bool TryResolve(Span<Cell> cells)
        {
            AssertMatchesBoard(cells);

            if (!Survey(cells, out int slotCount, out int pairLeft, out int pairRight))
                return false;

            Shuffle(cells, slotCount);
            ForceGroup(cells, slotCount, pairLeft, pairRight);

            AssertGroupExists(cells, pairLeft, pairRight);
            return true;
        }

        /// <summary>
        /// One walk over the board that produces everything the rest of the method needs: which cells
        /// hold colours, how often each colour occurs, and one pair of adjacent coloured cells.
        /// </summary>
        /// <remarks>
        /// Two passes would read the same, and at 100 cells cost the same. One pass is chosen because it
        /// makes the answer and the evidence for it inseparable: the feasibility check cannot pass while
        /// the guarantee step lacks the pair it needs, because the check <i>is</i> the pair.
        /// </remarks>
        private bool Survey(ReadOnlySpan<Cell> cells, out int slotCount, out int pairLeft, out int pairRight)
        {
            slotCount = 0;
            pairLeft = -1;
            pairRight = -1;

            int pairCount = 0;
            int bestColorCount = 0;

            for (int i = 0; i < cells.Length; i++)
            {
                if (!cells[i].IsColor) continue;

                slots[slotCount++] = i;

                int seen = ++colorCounts[cells[i].Color];
                if (seen > bestColorCount) bestColorCount = seen;

                // Up and right only, so each orthogonal pair is offered to the sampler exactly once. All
                // four directions would sample just as uniformly - every pair would simply be offered
                // twice - but it does double the work and makes the reader stop to prove that.
                TrySampleNeighborPair(cells, i, Grid.Up, ref pairCount, ref pairLeft, ref pairRight);
                TrySampleNeighborPair(cells, i, Grid.Right, ref pairCount, ref pairLeft, ref pairRight);
            }

            ClearCounts(cells, slotCount);

            // Both conditions are necessary and together they are sufficient. A colour that occurs twice
            // is what a group is made of; two adjacent coloured cells are where it can be put.
            return pairCount > 0 && bestColorCount >= GroupFinder.MinBlastableSize;
        }

        /// <summary>
        /// Reservoir sampling, k=1: after n candidates each has probability 1/n of being held.
        /// </summary>
        /// <remarks>
        /// Taking the first pair found would be simpler and would put the forced group in the same corner
        /// of the board every single time - a player sees that pattern within two or three shuffles and
        /// stops believing the board. Collecting all pairs and picking one would allocate a list whose
        /// size is not known up front. This needs neither: one candidate is held at a time.
        /// </remarks>
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

        /// <summary>
        /// Fisher-Yates over the coloured cells, swapping colours in place.
        /// </summary>
        /// <remarks>
        /// The random index is drawn only from the part of the range not yet fixed. Drawing from the whole
        /// range every time - <c>rng.Next(0, n)</c> - is the version most people write, and it is biased:
        /// it has n^n execution paths spread over n! permutations, and n^n does not divide by n!, so some
        /// orderings come up more often than others. At this scale the bias would never be noticed, which
        /// is precisely why it is worth getting right: it costs nothing.
        /// </remarks>
        private void Shuffle(Span<Cell> cells, int slotCount)
        {
            for (int i = slotCount - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);            // 0..i inclusive, the untouched region
                SwapColors(cells, slots[i], slots[j]);
            }
        }

        /// <summary>
        /// Puts the most common colour on both cells of the sampled pair, by swapping it in from
        /// elsewhere. This is what makes one pass enough.
        /// </summary>
        /// <remarks>
        /// It runs unconditionally, without first checking whether the shuffle happened to produce a
        /// group. Checking would save a swap or two and cost a second group scan when the check fails,
        /// but the real reason is determinism: one code path means "every shuffle finishes in exactly one
        /// pass" is a property of the code rather than a claim about the common case. The document
        /// forbids an unbounded loop; a single branchless path is the furthest thing from one.
        /// <para>
        /// The donor search cannot fail. The colour occurs at least twice (checked in <see cref="Survey"/>)
        /// and at most one of the two pair cells can already hold one of those occurrences, so there is
        /// always another cell to take one from.
        /// </para>
        /// </remarks>
        private void ForceGroup(Span<Cell> cells, int slotCount, int pairLeft, int pairRight)
        {
            byte color = MostCommonColor(cells, slotCount);

            PlaceColor(cells, slotCount, pairLeft, pairRight, color);
            PlaceColor(cells, slotCount, pairRight, pairLeft, color);
        }

        /// <summary>
        /// Gives <paramref name="target"/> the colour, taking it from some other cell that is neither the
        /// target nor <paramref name="partner"/> - the pair's other half, which must not be disturbed.
        /// </summary>
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

            // Unreachable while Survey's postcondition holds; loud rather than silent if it ever does not.
            throw new InvalidOperationException(
                $"No donor cell found for colour {color}, which Survey reported as occurring at least twice.");
        }

        private byte MostCommonColor(ReadOnlySpan<Cell> cells, int slotCount)
        {
            // Recounted rather than carried over from Survey: the shuffle has moved every colour since,
            // and while the totals are unchanged, a count that is rebuilt cannot be a count that went
            // stale. Same reasoning as the icon tiers in Karar 14.
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

        /// <summary>
        /// Zeroes only the histogram entries this pass touched, found through the cells themselves.
        /// <c>Array.Clear</c> over all 256 would also be correct and would do more work than the board has
        /// cells.
        /// </summary>
        private void ClearCounts(ReadOnlySpan<Cell> cells, int slotCount)
        {
            for (int s = 0; s < slotCount; s++) colorCounts[cells[slots[s]].Color] = 0;
        }

        private static void SwapColors(Span<Cell> cells, int a, int b)
        {
            byte held = cells[a].Color;
            cells[a].Color = cells[b].Color;   // in place: cells[i].X, never through a local copy
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

        /// <summary>
        /// The whole point of the class, stated where it is cheap to check: the two cells of the sampled
        /// pair are adjacent by construction, so equal colours there is a blastable group.
        /// </summary>
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