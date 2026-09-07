using System;
using UnityEngine;

namespace BlastGame.Game
{
    // Short cosmetic animations - a blasted block popping, its shards flying, a landed block squashing.
    // Built like FallAnimator on purpose: a preallocated struct array walked by one Tick, so the whole
    // effect layer costs one allocation at startup and nothing per frame.
    //
    // Why not ParticleSystem: a particle system draws with its own material, which splits the board's
    // single batch. These effects are pooled SpriteRenderers holding sprites from the block atlas, so
    // they batch with the board and add no draw call.
    //
    // Nothing here can change the game. Core has already resolved the move; dropping an effect on the
    // floor is a missing sparkle, never a wrong board.
    public sealed class EffectRunner
    {
        private enum Kind : byte
        {
            Pop,      // a blasted block: swells, then shrinks away while fading
            Shard,    // a fragment thrown from the blast, under gravity, spinning
            Squash    // a landed block flexing; the block belongs to the board, not to us
        }

        private struct Effect
        {
            public BlockView Block;
            public Kind Kind;

            // True when Block came from our own pool and must go back when the effect ends. A Squash
            // borrows a board block instead, and returning that one would delete it from the board.
            public bool Owned;

            // Shards integrate their own position; the board never hears about where they went.
            public Vector3 Position;
            public Vector2 Velocity;
            public float AngularVelocity;
            public float Rotation;

            public float Elapsed;
            public float Duration;
            public float Scale;
        }

        private const float ShardGravity = -22f;

        // Live effects packed into the first count slots, exactly like FallAnimator's moves.
        private readonly Effect[] effects;

        private int count;

        // Sprites for the owned effects. Separate from the board's pool by design: BoardView releases
        // a blasted block before renting the blocks that replace it, so holding one back for an
        // animation would starve the board's pool of the slot it is about to need.
        private readonly BlockPool pool;

        // The view's own randomness. Core's generator is seeded and reproducible, and a sparkle must
        // never be able to move it.
        private readonly System.Random rng = new System.Random();

        public EffectRunner(BlockPool pool, int capacity)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Needs at least one slot.");

            this.pool = pool;
            effects = new Effect[capacity];
        }

        public int ActiveCount => count;

        // A blasted block leaving the board: the same sprite in the same place, swelling and fading.
        public void Pop(Sprite sprite, Vector3 position, float duration)
        {
            if (sprite == null) return;

            if (!TryTakeSprite(sprite, position, 1f, out BlockView block)) return;

            Add(new Effect
            {
                Block = block,
                Kind = Kind.Pop,
                Owned = true,
                Position = position,
                Duration = duration,
                Scale = 1f
            });
        }

        // Fragments thrown from a blasted cell. Count is a request, not a promise - a full board
        // blasting at once asks for hundreds, and the cap is what keeps this bounded.
        public void Shards(Sprite sprite, Vector3 position, int requested, float scale, float speed, float duration)
        {
            if (sprite == null) return;

            for (int i = 0; i < requested; i++)
            {
                // A shard starts inside the cell it came from, not at its centre, or the burst looks
                // like a single point exploding rather than a block breaking up.
                Vector3 start = position + new Vector3(RandomRange(-0.22f, 0.22f), RandomRange(-0.22f, 0.22f), 0f);

                if (!TryTakeSprite(sprite, start, scale, out BlockView block)) return;   // cap reached

                // Biased upwards: gravity pulls them back down, and a burst that only spreads sideways
                // reads as a smear.
                var velocity = new Vector2(RandomRange(-speed, speed), RandomRange(speed * 0.35f, speed * 1.15f));

                Add(new Effect
                {
                    Block = block,
                    Kind = Kind.Shard,
                    Owned = true,
                    Position = start,
                    Velocity = velocity,
                    AngularVelocity = RandomRange(-320f, 320f),
                    Duration = duration * RandomRange(0.8f, 1.15f),
                    Scale = scale
                });
            }
        }

        // A block that just landed. The block stays the board's: we only write its scale, and we hand
        // it back at exactly 1 so nothing downstream has to know this happened.
        public void Squash(BlockView block, float amount, float duration)
        {
            if (block == null) return;

            // Landing twice in one duration would stack two writers on one scale.
            Cancel(block);

            if (count == effects.Length) return;

            Add(new Effect
            {
                Block = block,
                Kind = Kind.Squash,
                Owned = false,
                Duration = duration,
                Scale = amount
            });
        }

        // Called before a board block is pooled or redrawn. A Squash left running on a block that has
        // been rented out to another cell would keep writing that cell's scale.
        public void Cancel(BlockView block)
        {
            if (block == null) return;

            for (int i = count - 1; i >= 0; i--)
            {
                if (effects[i].Owned || effects[i].Block != block) continue;

                block.Scale = 1f;
                RemoveAt(i);
            }
        }

        // Drops every effect that writes a board block, leaving the owned ones running. For the
        // shuffle, which takes over every block's scale itself and would otherwise share it with a
        // squash still recovering from the move that caused the shuffle.
        public void CancelBorrowed()
        {
            for (int i = count - 1; i >= 0; i--)
            {
                if (effects[i].Owned) continue;

                effects[i].Block.Scale = 1f;
                RemoveAt(i);
            }
        }

        // Every owned sprite goes back to our pool; every borrowed block is restored. Called from a
        // redraw, which returns the board's blocks under us.
        public void Clear()
        {
            for (int i = 0; i < count; i++)
            {
                if (effects[i].Owned) pool.Return(effects[i].Block);
                else effects[i].Block.Scale = 1f;

                effects[i].Block = null;   // the array outlives the effect; don't pin the object
            }

            count = 0;
        }

        public void Tick(float deltaTime)
        {
            // Backwards, because finishing an effect swaps the last entry into this slot - the same
            // reason FallAnimator walks down.
            for (int i = count - 1; i >= 0; i--)
            {
                // By reference: Effect is a struct, and advancing a copy advances nothing.
                ref Effect effect = ref effects[i];

                effect.Elapsed += deltaTime;
                float t = effect.Elapsed / effect.Duration;

                if (t >= 1f)
                {
                    Finish(i);
                    continue;
                }

                switch (effect.Kind)
                {
                    case Kind.Pop: TickPop(ref effect, t); break;
                    case Kind.Shard: TickShard(ref effect, deltaTime, t); break;
                    case Kind.Squash: TickSquash(ref effect, t); break;
                }
            }
        }

        // Swells past its cell before collapsing: the overshoot is what makes a blast feel like a
        // release rather than a deletion.
        private static void TickPop(ref Effect effect, float t)
        {
            const float PeakAt = 0.3f;
            const float Peak = 1.25f;

            float scale = t < PeakAt
                ? Mathf.Lerp(1f, Peak, t / PeakAt)
                : Mathf.Lerp(Peak, 0f, (t - PeakAt) / (1f - PeakAt));

            effect.Block.Scale = scale;

            // Held opaque through the swell so the eye catches it, then gone quickly.
            effect.Block.Alpha = t < PeakAt ? 1f : 1f - (t - PeakAt) / (1f - PeakAt);
        }

        private static void TickShard(ref Effect effect, float deltaTime, float t)
        {
            effect.Velocity.y += ShardGravity * deltaTime;

            effect.Position.x += effect.Velocity.x * deltaTime;
            effect.Position.y += effect.Velocity.y * deltaTime;

            effect.Rotation += effect.AngularVelocity * deltaTime;

            effect.Block.Position = effect.Position;
            effect.Block.Rotation = effect.Rotation;

            // Fades only over the last third: a shard that starts disappearing on frame one never
            // reads as a solid piece of the block it came from.
            const float FadeFrom = 0.65f;
            effect.Block.Alpha = t < FadeFrom ? 1f : 1f - (t - FadeFrom) / (1f - FadeFrom);
        }

        // Hardest at the moment of impact, recovering from there. Squared so the recovery eases out
        // instead of stopping dead.
        private static void TickSquash(ref Effect effect, float t)
        {
            float k = 1f - t;
            k *= k;

            float amount = effect.Scale * k;
            effect.Block.SetScale(1f + amount, 1f - amount);
        }

        private bool TryTakeSprite(Sprite sprite, Vector3 position, float scale, out BlockView block)
        {
            block = null;

            // Both caps are real: the effect array, and the sprites available to fill it. Dropping the
            // effect is the correct answer to either - see the class comment.
            if (count == effects.Length) return false;
            if (pool.RentedCount == pool.Capacity) return false;

            block = pool.Rent();
            block.Sprite = sprite;
            block.Position = position;
            block.Scale = scale;

            return true;
        }

        private void Add(in Effect effect)
        {
            effects[count] = effect;
            count++;
        }

        private void Finish(int index)
        {
            ref Effect effect = ref effects[index];

            if (effect.Owned) pool.Return(effect.Block);
            else effect.Block.Scale = 1f;   // hand the board back a block at rest

            RemoveAt(index);
        }

        private void RemoveAt(int index)
        {
            effects[index].Block = null;

            count--;
            if (index == count) return;

            effects[index] = effects[count];
            effects[count].Block = null;
        }

        private float RandomRange(float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
