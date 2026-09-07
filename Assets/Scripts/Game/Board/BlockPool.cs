using System;
using UnityEngine;

namespace BlastGame.Game
{
    // A fixed set of blocks, created once and then only activated and deactivated. The point is the
    // absence of Instantiate and Destroy during play; a blast churns a dozen blocks per move.
    public sealed class BlockPool
    {
        // A stack, so the most recently returned object comes back first and is likelier to be warm in
        // cache. Neither order is observable.
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
                BlockView block = UnityEngine.Object.Instantiate(prefab, parent);
                block.gameObject.name = "Block";     // otherwise every object reads "Block(Clone)"
                block.gameObject.SetActive(false);

                idle[i] = block;
            }

            idleCount = capacity;
        }

        // Throws rather than growing. Capacity comes from the board, so running out means the view
        // leaked a block, and a pool that allocates its way out hides the bug it exists to catch.
        public BlockView Rent()
        {
            if (idleCount == 0)
                throw new InvalidOperationException(
                    $"Block pool exhausted at {Capacity} blocks; a rented block was never returned.");

            BlockView block = idle[--idleCount];

            // Effects hand blocks back mid-flight - a pop leaves one shrunken and transparent, a shard
            // leaves one turned. Reset everything an effect can write.
            block.Scale = 1f;
            block.Rotation = 0f;
            block.Color = Color.white;

            block.gameObject.SetActive(true);
            return block;
        }

        public void Return(BlockView block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));

            // Reachable by returning the same block twice, which shows up as a missing block somewhere
            // else entirely.
            if (idleCount == idle.Length)
                throw new InvalidOperationException("Returned a block to a full pool; it was already idle.");

            block.gameObject.SetActive(false);
            idle[idleCount++] = block;
        }
    }
}
