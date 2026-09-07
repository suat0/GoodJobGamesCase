using System;
using UnityEngine;

namespace BlastGame.Game
{
    // Moves blocks from where they were drawn to where the board says they now are. One loop for the
    // whole board; the per-block Update version gets slower as the board grows.
    //
    // Purely cosmetic - Core finished the move before this class heard about it.
    //
    // Blocks accelerate under gravity. Duration still comes from distance, sqrt(2d/g), which is what
    // rules out a fixed-duration ease: a long fall has to take longer than a short one.
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

        private readonly Move[] moves;

        private int count;

        private readonly int[] entryOfCell;

        private readonly float gravity;   // cells per second squared

        private readonly Action<BlockView> onLanded;

        public FallAnimator(int cellCount, float gravity, Action<BlockView> onLanded = null)
        {
            if (cellCount < 1) throw new ArgumentOutOfRangeException(nameof(cellCount));
            if (gravity <= 0f) throw new ArgumentOutOfRangeException(nameof(gravity), gravity, "Gravity must be positive.");

            this.gravity = gravity;
            this.onLanded = onLanded;

            moves = new Move[cellCount];

            entryOfCell = new int[cellCount];
            ClearCellEntries();
        }

        // The one place the view's notion of time feeds a decision. "Settled" is a fact about an
        // animation, so Core neither knows nor should know it.
        public bool IsSettled(int cell) => entryOfCell[cell] < 0;

        public void Begin(BlockView block, Vector3 from, Vector3 to, int targetCell)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));

            // Would mean two blocks racing to the same square, one of them never released.
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

                // d = gt^2/2 solved for t. A block redirected mid-air starts over from rest, which is
                // also what it looks like when the ground disappears from under it.
                Duration = Mathf.Sqrt(2f * distance / gravity),
                Elapsed = 0f,
                TargetCell = targetCell
            };

            entryOfCell[targetCell] = count;
            count++;

            block.Position = from;
        }

        // Leaves the block where it is: the caller either pools it or starts a new move from there,
        // and snapping to the target first would be a visible jump.
        public void Cancel(int cell)
        {
            int entry = entryOfCell[cell];
            if (entry < 0) return;

            entryOfCell[cell] = -1;
            RemoveAt(entry);
        }

        // Blocks stay where they are; the caller is redrawing the board anyway.
        public void Clear()
        {
            count = 0;
            ClearCellEntries();
        }

        public void Tick(float deltaTime)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                // By reference - Move is a struct, and += on a copy would advance nothing.
                ref Move move = ref moves[i];

                move.Elapsed += deltaTime;
                float t = move.Elapsed / move.Duration;

                if (t < 1f)
                {
                    move.Block.Position = Vector3.Lerp(move.From, move.To, t * t);
                    continue;
                }

                // The target itself rather than Lerp(..., 1). A float that merely rounds to the cell
                // centre would drift over a session.
                move.Block.Position = move.To;

                BlockView landed = move.Block;

                entryOfCell[move.TargetCell] = -1;
                RemoveAt(i);

                onLanded?.Invoke(landed);
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
            // -1 because slot 0 is valid, so "nothing here" needs a different value.
            for (int i = 0; i < entryOfCell.Length; i++) entryOfCell[i] = -1;
        }
    }
}
