using System;
using UnityEngine;

namespace BlastGame.Game
{
    /// <summary>
    /// A fixed set of block objects, created once and then only ever activated and deactivated.
    /// </summary>
    /// <remarks>
    /// <b>The point is the absence of Instantiate and Destroy during play.</b> Both are expensive - a
    /// managed allocation, a native object, component wiring, and eventually a garbage collection that
    /// lands in the middle of an animation. A blast removes and creates a dozen blocks; doing that with
    /// Instantiate/Destroy every move is exactly the cost the case document asks about.
    /// <para>
    /// Not a MonoBehaviour. It has no Update, no inspector state and no reason to be findable in the
    /// scene: <see cref="BoardView"/> owns it, and that is the whole story (CLAUDE.md, simplicity rule).
    /// </para>
    /// </remarks>
    public sealed class BlockPool
    {
        /// <summary>
        /// Idle blocks, used as a stack. A stack rather than a queue because the most recently returned
        /// object is the one most likely still warm in cache, and neither order is observable anyway.
        /// </summary>
        private readonly BlockView[] idle;

        private int idleCount;

        public int Capacity => idle.Length;

        /// <summary>How many blocks are currently handed out. Read by the draw-call/allocation checks.</summary>
        public int RentedCount => idle.Length - idleCount;

        /// <summary>
        /// Creates every block up front and parks it inactive.
        /// </summary>
        /// <param name="parent">
        /// Kept as a child so the hierarchy stays readable, and so a whole board can be discarded by
        /// destroying one object.
        /// </param>
        public BlockPool(BlockView prefab, Transform parent, int capacity)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Pool needs at least one block.");

            idle = new BlockView[capacity];

            for (int i = 0; i < capacity; i++)
            {
                // Instantiated active so BlockView.Awake runs now rather than the first time a block is
                // rented, which would move the work back into play - the thing this class exists to avoid.
                BlockView block = UnityEngine.Object.Instantiate(prefab, parent);
                block.gameObject.name = "Block";     // otherwise every object reads "Block(Clone)"
                block.gameObject.SetActive(false);

                idle[i] = block;
            }

            idleCount = capacity;
        }

        /// <summary>
        /// Takes an idle block and activates it. The caller sets its sprite and position.
        /// </summary>
        /// <remarks>
        /// Throws instead of growing. The capacity is derived from the board, so running out is not a
        /// load spike to absorb - it means the view leaked a block, and a pool that quietly allocates
        /// its way out of that hides the bug it was built to make impossible.
        /// </remarks>
        public BlockView Rent()
        {
            if (idleCount == 0)
                throw new InvalidOperationException(
                    $"Block pool exhausted at {Capacity} blocks; a rented block was never returned.");

            BlockView block = idle[--idleCount];
            block.gameObject.SetActive(true);
            return block;
        }

        /// <summary>Parks a block. Its sprite is left alone - the next renter overwrites it.</summary>
        public void Return(BlockView block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));

            // Only reachable by returning the same block twice, which would then hand it out to two
            // cells at once - a bug that renders as a missing block somewhere else entirely.
            if (idleCount == idle.Length)
                throw new InvalidOperationException("Returned a block to a full pool; it was already idle.");

            block.gameObject.SetActive(false);
            idle[idleCount++] = block;
        }
    }
}
