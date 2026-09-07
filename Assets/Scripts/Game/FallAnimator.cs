using System;
using UnityEngine;

namespace BlastGame.Game
{
    // Moves blocks from where they were drawn to where the board says they now are. One loop for
    // every block, not one Update per block - the per-block version gets slower with the board.
    // Purely cosmetic: Core finished the move before this class heard about it.
    // Constant speed, not a curve - duration comes from distance, so everything moves at the same
    // rate. A fixed-duration ease would make long falls visibly faster than short ones.
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

        // Live moves packed into the first count slots. Struct array, not a list of objects: one
        // allocation at startup, and the frame's work walks contiguous memory.
        private readonly Move[] moves;

        private int count;

        // Which slot is heading for a cell, or -1 when nothing is. Makes "settled?" a single array
        // read; a cell holds at most one move because no two blocks land on the same square.
        private readonly int[] entryOfCell;

        private readonly float speed;   // cells per second

        public FallAnimator(int cellCount, float speed)
        {
            if (cellCount < 1) throw new ArgumentOutOfRangeException(nameof(cellCount));
            if (speed <= 0f) throw new ArgumentOutOfRangeException(nameof(speed), speed, "Speed must be positive.");

            this.speed = speed;

            moves = new Move[cellCount];

            entryOfCell = new int[cellCount];
            ClearCellEntries();
        }

        // The one place the view's notion of time feeds a decision. Core has no idea which blocks are
        // mid-air and must not - "settled" is a fact about an animation.
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
                Duration = distance / speed,
                Elapsed = 0f,
                TargetCell = targetCell
            };

            entryOfCell[targetCell] = count;
            count++;

            block.Position = from;
        }

        // For a block blasted mid-fall or redirected. Deliberately leaves it where it is: the caller
        // either pools it or starts a new move from there, and snapping first would be a visible jump.
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
            // Backwards, because finishing a move swaps the last entry into the current slot. Walking
            // down means that entry has already been handled this frame.
            for (int i = count - 1; i >= 0; i--)
            {
                // By reference: Move is a struct, and moves[i].Elapsed += dt on a copy would advance
                // nothing at all.
                ref Move move = ref moves[i];

                move.Elapsed += deltaTime;
                float t = move.Elapsed / move.Duration;

                if (t < 1f)
                {
                    move.Block.Position = Vector3.Lerp(move.From, move.To, t);
                    continue;
                }

                // The target itself, not Lerp(..., 1): a float that merely rounds to the cell centre
                // would drift over a session.
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
            // -1, not 0: slot 0 is valid, so "nothing here" has to be a different value.
            for (int i = 0; i < entryOfCell.Length; i++) entryOfCell[i] = -1;
        }
    }
}
