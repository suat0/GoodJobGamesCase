using System;
using UnityEngine;

namespace BlastGame.Game
{
    // A fixed set of block objects, created once and then only activated and deactivated. The point
    // is the absence of Instantiate and Destroy during play: a blast churns a dozen blocks per move.
    // Not a MonoBehaviour - no Update, no inspector state, and BoardView owns it.
    public sealed class BlockPool
    {
        // Used as a stack rather than a queue: the most recently returned object is the one most likely
        // still warm in cache, and neither order is observable.
        private readonly BlockView[] idle;

        private int idleCount;

        public int Capacity => idle.Length;

        public int RentedCount => idle.Length - idleCount;

        public BlockPool(BlockView prefab, Transform parent, int capacity)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Pool needs at least one block.");

            idle = new BlockView[capacity];

            for (int i = 0; i < capacity; i++)
            {
                // Instantiated active so BlockView.Awake runs now rather than on the first rent, which
                // would move the work back into play.
                BlockView block = UnityEngine.Object.Instantiate(prefab, parent);
                block.gameObject.name = "Block";     // otherwise every object reads "Block(Clone)"
                block.gameObject.SetActive(false);

                idle[i] = block;
            }

            idleCount = capacity;
        }

        // Throws instead of growing: capacity is derived from the board, so running out means the view
        // leaked a block. A pool that allocates its way out hides the bug it exists to prevent.
        public BlockView Rent()
        {
            if (idleCount == 0)
                throw new InvalidOperationException(
                    $"Block pool exhausted at {Capacity} blocks; a rented block was never returned.");

            BlockView block = idle[--idleCount];

            // The shuffle feedback leaves scales part-way, so a block returned mid-animation would
            // otherwise reappear shrunken.
            block.Scale = 1f;

            block.gameObject.SetActive(true);
            return block;
        }

        public void Return(BlockView block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));

            // Reachable only by returning the same block twice, which renders as a missing block
            // somewhere else entirely.
            if (idleCount == idle.Length)
                throw new InvalidOperationException("Returned a block to a full pool; it was already idle.");

            block.gameObject.SetActive(false);
            idle[idleCount++] = block;
        }
    }
}
