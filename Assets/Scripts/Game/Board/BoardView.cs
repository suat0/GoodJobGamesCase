using System;
using BlastGame.Core;
using UnityEngine;

namespace BlastGame.Game
{
    // Draws a Board: one pooled sprite per non-empty cell. Reads Core, never writes it.
    //
    // One world unit per cell (256px sprites at 256 PPU). The board itself is never scaled - the
    // camera is fitted to it - because transform scale would leak into the input maths and into every
    // fall distance.
    public sealed class BoardView : MonoBehaviour
    {
        public const float CellSize = 1f;

        [Serializable]
        private sealed class ColorSprites
        {
            public Sprite defaultIcon;
            public Sprite iconA;
            public Sprite iconB;
            public Sprite iconC;

            public Sprite ForTier(int tier)
            {
                switch (tier)
                {
                    case 3: return iconC;
                    case 2: return iconB;
                    case 1: return iconA;
                    default: return defaultIcon;
                }
            }
        }

        [Header("Wiring")]
        [SerializeField] private GameController controller;

        [Header("Blocks")]
        [SerializeField] private BlockView blockPrefab;

        [Tooltip("Indexed by Core's colour index. Must cover the level's ColorCount.")]
        [SerializeField] private ColorSprites[] colorSprites;

        [Tooltip("Indexed by damage taken: element 0 is an undamaged Box.")]
        [SerializeField] private Sprite[] boxSprites;

        [Header("Framing")]
        [SerializeField] private Camera boardCamera;

        [Tooltip("World units of empty space around the board.")]
        [SerializeField] private float cameraPadding = 0.5f;

        [Tooltip("Nine-sliced panel drawn behind the grid. Optional, and decorative only - it is " +
                 "sized here because only this class knows how big the board turned out to be.")]
        [SerializeField] private SpriteRenderer boardFrame;

        [SerializeField] private float framePadding = 0.3f;

        [Header("Motion")]
        [Tooltip("Fall acceleration in cells per second squared. Distance still sets the duration, " +
                 "so a long fall takes longer - it just does not travel at a constant rate.")]
        [SerializeField] private float fallGravity = 55f;

        [Tooltip("Seconds for the whole shuffle: blocks shrink away, the board is redrawn, they grow back.")]
        [SerializeField] private float shuffleDuration = 0.4f;

        [Header("Effects")]
        [Tooltip("Sprites reserved for blast effects. The cap is deliberate: a full board blasting at " +
                 "once would otherwise ask for hundreds. Effects past it are dropped, not queued.")]
        [SerializeField] private int effectCapacity = 128;

        [Tooltip("Seconds a blasted block takes to swell and vanish.")]
        [SerializeField] private float popDuration = 0.16f;

        [SerializeField] private int shardsPerBlock = 3;
        [SerializeField] private int shardsPerBox = 6;
        [SerializeField] private float shardScale = 0.3f;
        [SerializeField] private float shardSpeed = 3.5f;
        [SerializeField] private float shardDuration = 0.45f;

        [Tooltip("How far a landing block flexes, as a fraction of a cell. Cosmetic only: a squashing " +
                 "block is settled, so it stays tappable the frame it lands.")]
        [SerializeField] private float landingSquash = 0.18f;

        [SerializeField] private float landingSquashDuration = 0.12f;

        [Header("Camera shake")]
        [Tooltip("Cell fractions the camera swings at the start of a shake.")]
        [SerializeField] private float shakeMagnitude = 0.09f;

        [SerializeField] private float shakeDuration = 0.22f;

        [Tooltip("Blocks removed in one move before the blast alone earns a shake. Shaking on every " +
                 "move would make the whole game feel unsteady; a Box breaking always shakes.")]
        [SerializeField] private int shakeBlastThreshold = 7;

        [Tooltip("Depth offset for effect sprites. Same sorting layer and material as the board, so " +
                 "the batch holds; z alone decides that a shard draws in front of the blocks.")]
        [SerializeField] private float effectDepth = -0.1f;

        private Board board;
        private BlockPool pool;
        private FallAnimator fallAnimator;

        private BlockPool effectPool;
        private EffectRunner effectRunner;

        private BlockView[] movingBlocks;

        private BlockView[] blockAt;

        // Centre of cell (0, 0) in world space. Row 0 is the bottom row, as everywhere else.
        private Vector3 origin;

        private float shuffleElapsed = NotShuffling;

        private BoardCamera framing;

        private bool shuffleRedrawn;

        private const float NotShuffling = -1f;

        private bool IsShuffling => shuffleElapsed >= 0f;

        // OnEnable/OnDisable rather than Start/OnDestroy, and named methods rather than lambdas. An
        // object disabled and re-enabled would otherwise subscribe twice, and a lambda cannot be
        // unsubscribed at all.
        private void OnEnable()
        {
            controller.OnBoardReady += HandleBoardReady;
            controller.OnBoardChanged += HandleBoardChanged;
            controller.OnDeadlockResolved += HandleDeadlockResolved;
        }

        private void OnDisable()
        {
            controller.OnBoardReady -= HandleBoardReady;
            controller.OnBoardChanged -= HandleBoardChanged;
            controller.OnDeadlockResolved -= HandleDeadlockResolved;
        }

        private void HandleBoardReady(Board readyBoard)
        {
            Bind(readyBoard);
            Redraw();
        }

        private void HandleBoardChanged(BlastResult result) => ApplyBlast(result);

        private void HandleDeadlockResolved() => BeginShuffleAnimation();

        public void Bind(Board newBoard)
        {
            board = newBoard ?? throw new ArgumentNullException(nameof(newBoard));

            ValidateSprites();

            origin = transform.position + new Vector3(
                -(board.Cols - 1) * 0.5f * CellSize,
                -(board.Rows - 1) * 0.5f * CellSize,
                0f);

            // Built once. A restart regenerates the same board object, so rebuilding here would strand
            // a board's worth of objects and allocate a second set.
            if (pool == null)
            {
                blockAt = new BlockView[board.CellCount];

                movingBlocks = new BlockView[board.CellCount];

                fallAnimator = new FallAnimator(board.CellCount, fallGravity, HandleBlockLanded);

                // Redraw returns every block before renting any, so the peak is exactly CellCount.
                pool = new BlockPool(blockPrefab, transform, board.CellCount);

                // A pool of its own rather than headroom in the board's. ApplyBlast releases the
                // blasted blocks before renting the ones that replace them, so an effect holding one
                // back would starve the board of the slot it is about to need.
                var effectsRoot = new GameObject("Effects").transform;
                effectsRoot.SetParent(transform, false);

                effectPool = new BlockPool(blockPrefab, effectsRoot, effectCapacity);
                effectRunner = new EffectRunner(effectPool, effectCapacity);

                framing = new BoardCamera(boardCamera, cameraPadding, shakeMagnitude, shakeDuration);
            }

            framing.Frame(board.Rows, board.Cols, CellSize, transform.position);
            FitFrame();
        }

        // A full rebuild, for the first draw and the post-shuffle redraw. Ordinary moves go through
        // ApplyBlast and touch only what changed.
        public void Redraw()
        {
            if (board == null) throw new InvalidOperationException("Redraw before Bind.");

            fallAnimator.Clear();

            // Before the blocks are pooled. An effect borrowing one has to give it back at rest, or
            // the next cell to rent it inherits a squashed scale.
            effectRunner.Clear();

            for (int i = 0; i < blockAt.Length; i++)
            {
                if (blockAt[i] == null) continue;

                pool.Return(blockAt[i]);
                blockAt[i] = null;
            }

            for (int i = 0; i < blockAt.Length; i++)
            {
                Sprite sprite = SpriteFor(i);
                if (sprite == null) continue;          // an empty cell: a hole under a Box

                BlockView block = pool.Rent();
                block.Sprite = sprite;
                block.Position = CellToWorld(i);

                blockAt[i] = block;
            }
        }

        // Catches up with a move Core already resolved. result is read here and never kept.
        // Detach before attach, or a block leaving cell 40 and another arriving on it - the same move -
        // would hand one block to two cells.
        public void ApplyBlast(BlastResult result)
        {
            if (board == null) throw new InvalidOperationException("ApplyBlast before Bind.");

            ReleaseBlocksAt(result.Removed, shardsPerBlock);

            // A Box takes two moves to break, so its one break is worth more than a colour block's.
            ReleaseBlocksAt(result.BrokenBoxes, shardsPerBox);

            ReadOnlySpan<int> from = result.MoveFrom;
            ReadOnlySpan<int> to = result.MoveTo;

            for (int i = 0; i < from.Length; i++)
            {
                int source = from[i];

                if (result.IsSpawn(source))
                {
                    BlockView spawned = pool.Rent();
                    spawned.Position = CellToWorld(source);

                    movingBlocks[i] = spawned;
                    continue;
                }

                // Still in the air from an earlier move. Cancelling leaves it where it is and the new
                // fall starts from there, so a fast player sees a redirection rather than a jump.
                fallAnimator.Cancel(source);

                movingBlocks[i] = blockAt[source];
                blockAt[source] = null;
            }

            for (int i = 0; i < to.Length; i++)
            {
                int target = to[i];
                BlockView block = movingBlocks[i];

                blockAt[target] = block;
                fallAnimator.Begin(block, block.Position, CellToWorld(target), target);

                movingBlocks[i] = null;   // nothing here outlives the call
            }

            RefreshSprites();

            // A Box break costs two moves to earn, so it always lands. An ordinary blast has to be big
            // before it gets the same treatment.
            if (result.BrokenBoxes.Length > 0) framing.Shake(1f);
            else if (result.Removed.Length >= shakeBlastThreshold) framing.Shake(0.6f);
        }

        // Here because this class owns both halves of the mapping - the layout, and the animator that
        // knows which blocks have landed.
        //
        // The settled filter asks about the tapped cell alone, never the group: landed blocks stay
        // tappable while others fall, so a group with one member mid-air must still blast.
        public bool TryPickCell(Vector3 screenPosition, out int cellIndex)
        {
            cellIndex = -1;
            if (board == null) return false;

            if (IsShuffling) return false;

            Vector3 world = boardCamera.ScreenToWorldPoint(screenPosition);

            // origin is the centre of cell (0,0), so half a cell shifts it to the lower-left corner.
            // Floor rather than a cast, which truncates towards zero and folds -0.4 onto cell 0.
            int col = Mathf.FloorToInt((world.x - origin.x) / CellSize + 0.5f);
            int row = Mathf.FloorToInt((world.y - origin.y) / CellSize + 0.5f);

            if (row < 0 || row >= board.Rows || col < 0 || col >= board.Cols) return false;

            int candidate = row * board.Cols + col;
            if (!fallAnimator.IsSettled(candidate)) return false;

            cellIndex = candidate;
            return true;
        }

        // The single per-frame loop for the whole board. No block has an Update of its own.
        private void Update()
        {
            if (fallAnimator == null) return;   // before Bind

            float deltaTime = Time.deltaTime;

            fallAnimator.Tick(deltaTime);
            effectRunner.Tick(deltaTime);
            TickShuffleAnimation(deltaTime);
            framing.Tick(deltaTime);
        }

        // A shuffle swaps colour values rather than moving blocks, so without feedback the whole board
        // would change identity between two frames and read as a glitch. Shrink-and-grow carries the
        // same information as flying each colour to its cell, for a tenth of the work.
        //
        // Scale is written per block, never on this transform - a scaled parent would stop one world
        // unit meaning one cell, and a child under a zero-scaled parent is a division by zero.
        private void BeginShuffleAnimation()
        {
            // Landing squashes from the move that caused the shuffle are still running, and the
            // shuffle is about to take over every block's scale.
            effectRunner.CancelBorrowed();

            shuffleElapsed = 0f;
            shuffleRedrawn = false;
        }

        private void TickShuffleAnimation(float deltaTime)
        {
            if (!IsShuffling) return;

            shuffleElapsed += deltaTime;
            float t = shuffleElapsed / shuffleDuration;

            if (t >= 1f)
            {
                if (!shuffleRedrawn) Redraw();   // a duration short enough to skip the midpoint frame

                shuffleElapsed = NotShuffling;
                SetAllBlockScales(1f);
                return;
            }

            // Swapped while nothing is on screen, which also hides Redraw clearing the fall animator.
            if (t >= 0.5f && !shuffleRedrawn)
            {
                Redraw();
                shuffleRedrawn = true;
            }

            // 1 at both ends and 0 in the middle - one expression for shrink-then-grow, no phase flag.
            SetAllBlockScales(Easing.SmoothStep(Mathf.Abs(1f - 2f * t)));
        }

        // The animator has already released the cell, so the block is settled and tappable while this
        // runs. Impact feedback must never cost a tap.
        private void HandleBlockLanded(BlockView block) =>
            effectRunner.Squash(block, landingSquash, landingSquashDuration);

        private void SetAllBlockScales(float scale)
        {
            for (int i = 0; i < blockAt.Length; i++)
            {
                if (blockAt[i] == null) continue;

                blockAt[i].Scale = scale;
            }
        }

        // The effect copies the sprite and position and runs on a sprite of its own, so the board's
        // block is free immediately and nothing downstream waits for an animation.
        private void ReleaseBlocksAt(ReadOnlySpan<int> cells, int shardCount)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                int cell = cells[i];

                BlockView block = blockAt[cell];
                if (block == null) continue;

                Vector3 where = block.Position;
                where.z += effectDepth;

                effectRunner.Pop(block.Sprite, where, popDuration);
                effectRunner.Shards(block.Sprite, where, shardCount, shardScale, shardSpeed, shardDuration);

                // A block blasted within a squash of landing still has an effect writing its scale.
                effectRunner.Cancel(block);

                fallAnimator.Cancel(cell);
                pool.Return(block);
                blockAt[cell] = null;
            }
        }

        // Every block, not only the ones that moved. A group growing several columns away changes the
        // icon tier of blocks that did not move, so the move list cannot answer "what changed".
        // Damaged Boxes need no case of their own - the same lookup returns the cracked sprite.
        private void RefreshSprites()
        {
            for (int i = 0; i < blockAt.Length; i++)
            {
                if (blockAt[i] == null) continue;

                blockAt[i].Sprite = SpriteFor(i);
            }
        }

        // Valid for rows above the board, which is where new blocks start.
        public Vector3 CellToWorld(int index)
        {
            int row = index / board.Cols;
            int col = index - row * board.Cols;

            return origin + new Vector3(col * CellSize, row * CellSize, 0f);
        }

        // Tier is asked of Core per cell rather than cached: three comparisons over data Core already
        // has, where a local copy would go stale silently, as a wrong sprite.
        private Sprite SpriteFor(int index)
        {
            Cell cell = board.CellAt(index);

            if (cell.IsBox) return boxSprites[Cell.BoxMaxHealth - cell.Health];
            if (!cell.IsColor) return null;

            return colorSprites[cell.Color].ForTier(board.TierAt(index));
        }

        // Sized from the board rather than authored in the scene, so a 2x2 level and a 10x10 level
        // both get a frame that fits without anyone remembering to resize it.
        private void FitFrame()
        {
            if (boardFrame == null) return;

            boardFrame.transform.position = transform.position;
            boardFrame.size = new Vector2(
                board.Cols * CellSize + framePadding * 2f,
                board.Rows * CellSize + framePadding * 2f);
        }

        // Checked once, at bind time. An unassigned sprite otherwise surfaces as an invisible block or
        // a null reference from inside the draw loop, naming no colour.
        private void ValidateSprites()
        {
            if (blockPrefab == null) throw new InvalidOperationException($"{name}: block prefab is not assigned.");
            if (boardCamera == null) throw new InvalidOperationException($"{name}: board camera is not assigned.");

            if (boxSprites == null || boxSprites.Length != Cell.BoxMaxHealth)
                throw new InvalidOperationException(
                    $"{name}: needs exactly {Cell.BoxMaxHealth} Box sprites, one per damage level.");

            // The board is the authority on how many colours are in play: a four-colour level must not
            // be rejected for having six sprite slots, or accepted with only three filled.
            int highestColor = -1;
            for (int i = 0; i < board.CellCount; i++)
            {
                Cell cell = board.CellAt(i);
                if (cell.IsColor && cell.Color > highestColor) highestColor = cell.Color;
            }

            if (colorSprites == null || highestColor >= colorSprites.Length)
                throw new InvalidOperationException(
                    $"{name}: board uses colour index {highestColor} but only {colorSprites?.Length ?? 0} " +
                    "colour sprite sets are assigned.");

            for (int color = 0; color <= highestColor; color++)
            {
                ColorSprites set = colorSprites[color];

                if (set == null || set.defaultIcon == null || set.iconA == null || set.iconB == null || set.iconC == null)
                    throw new InvalidOperationException($"{name}: colour {color} is missing one of its four sprites.");
            }
        }
    }
}
