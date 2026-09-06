using System;
using UnityEngine;

namespace BlastGame.Game
{
    /// <summary>
    /// Moves blocks from where they were drawn to where the board says they now are.
    /// </summary>
    /// <remarks>
    /// <b>One loop for every block, not one Update per block.</b> A hundred MonoBehaviours with an
    /// Update each means a hundred managed-to-native calls per frame plus the list Unity keeps to make
    /// them; this is one loop over a struct array. At this scale neither is slow - the point is that the
    /// per-block version gets slower with the board and this one does not.
    /// <para>
    /// <b>Purely cosmetic.</b> Core finished the move before this class heard about it, so an animation
    /// can run as long as it likes without the board being in a half-finished state (Karar 5). Nothing
    /// here can change a cell; it only interpolates positions.
    /// </para>
    /// <para>
    /// <b>Constant speed, not a curve.</b> Duration is derived from distance, so a block falling five
    /// cells takes five times as long as one falling a single cell and everything on screen moves at the
    /// same rate. An eased curve with a fixed duration would make long falls visibly faster than short
    /// ones - the board would look like it was made of different materials (Karar 5b).
    /// </para>
    /// </remarks>
    public sealed class FallAnimator
    {
        private struct Move
        {
            public BlockView Block;
            public Vector3 From;
            public Vector3 To;
            public float Duration;
            public float Elapsed;
            public int TargetCell;
        }

        /// <summary>
        /// Live moves, packed into the first <see cref="count"/> slots. A struct array rather than a list
        /// of objects: one allocation at startup, and the whole frame's work walks contiguous memory.
        /// </summary>
        private readonly Move[] moves;

        private int count;

        /// <summary>
        /// Which slot in <see cref="moves"/> is heading for a cell, or -1 when nothing is. This is what
        /// makes "is the block on this cell settled?" a single array read instead of a scan, and a cell
        /// can hold at most one move because no two blocks land on the same square.
        /// </summary>
        private readonly int[] entryOfCell;

        private readonly float speed;

        /// <param name="speed">Cells per second. Every block moves at this rate, whatever the distance.</param>
        public FallAnimator(int cellCount, float speed)
        {
            if (cellCount < 1) throw new ArgumentOutOfRangeException(nameof(cellCount));
            if (speed <= 0f) throw new ArgumentOutOfRangeException(nameof(speed), speed, "Speed must be positive.");

            this.speed = speed;

            // A cell can be the target of at most one move, so the board's cell count is the ceiling.
            moves = new Move[cellCount];

            entryOfCell = new int[cellCount];
            ClearCellEntries();
        }

        /// <summary>Whether the block drawn on this cell has finished moving. The B2 filter reads this.</summary>
        /// <remarks>
        /// The one place the view's notion of time leaks into a decision. Core has no idea which blocks
        /// are mid-air and must not: "settled" is a fact about an animation, and animations are this
        /// side's business (CLAUDE.md, architecture rule 3).
        /// </remarks>
        public bool IsSettled(int cell) => entryOfCell[cell] < 0;

        /// <summary>Starts moving a block to a cell. A distance of zero is placed instantly rather than tracked.</summary>
        public void Begin(BlockView block, Vector3 from, Vector3 to, int targetCell)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));

            // Only reachable if a caller left an old move pointing at this cell, which would mean two
            // blocks racing to the same square and one of them never being released.
            if (entryOfCell[targetCell] >= 0)
                throw new InvalidOperationException($"Cell {targetCell} already has a block in flight.");

            float distance = Vector3.Distance(from, to);
            if (distance <= Mathf.Epsilon)
            {
                block.Position = to;
                return;
            }

            moves[count] = new Move
            {
                Block = block,
                From = from,
                To = to,
                Duration = distance / speed,
                Elapsed = 0f,
                TargetCell = targetCell
            };

            entryOfCell[targetCell] = count;
            count++;

            block.Position = from;
        }

        /// <summary>
        /// Forgets the move aimed at a cell, leaving the block wherever it currently is.
        /// </summary>
        /// <remarks>
        /// Used when a block is blasted mid-fall or told to fall somewhere else. It deliberately does not
        /// snap the block to its old target: the caller either returns it to the pool or starts a new move
        /// from its current position, and snapping first would be a visible jump backwards.
        /// </remarks>
        public void Cancel(int cell)
        {
            int entry = entryOfCell[cell];
            if (entry < 0) return;

            entryOfCell[cell] = -1;
            RemoveAt(entry);
        }

        /// <summary>Drops every move. Blocks stay where they are; the caller is redrawing the board anyway.</summary>
        public void Clear()
        {
            count = 0;
            ClearCellEntries();
        }

        /// <summary>Advances every live move by one frame.</summary>
        public void Tick(float deltaTime)
        {
            // Backwards, because finishing a move swaps the last entry into the current slot. Walking
            // down means that entry has already been handled this frame, so nothing is visited twice
            // and nothing is skipped.
            for (int i = count - 1; i >= 0; i--)
            {
                // By reference: Move is a struct, and moves[i].Elapsed += dt on a copy would advance
                // nothing at all (CLAUDE.md, copy trap).
                ref Move move = ref moves[i];

                move.Elapsed += deltaTime;
                float t = move.Elapsed / move.Duration;

                if (t < 1f)
                {
                    move.Block.Position = Vector3.Lerp(move.From, move.To, t);
                    continue;
                }

                // Assign the target itself rather than Lerp(..., 1): the block has to land on the exact
                // cell centre, and a float that merely rounds to it would drift over a session.
                move.Block.Position = move.To;

                entryOfCell[move.TargetCell] = -1;
                RemoveAt(i);
            }
        }

        private void RemoveAt(int entry)
        {
            count--;
            if (entry == count) return;

            moves[entry] = moves[count];
            entryOfCell[moves[entry].TargetCell] = entry;
        }

        private void ClearCellEntries()
        {
            // Filled with -1 rather than cleared to 0, for the same reason as GroupFinder's group ids:
            // 0 is a valid slot, so "nothing here" has to be a different value.
            for (int i = 0; i < entryOfCell.Length; i++) entryOfCell[i] = -1;
        }
    }
}
