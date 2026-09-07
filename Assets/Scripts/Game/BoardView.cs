using System;
using BlastGame.Core;
using UnityEngine;

namespace BlastGame.Game
{
    // Draws a Board: one pooled sprite per non-empty cell. Reads Core, never writes it.
    // One world unit per cell (256px sprites at 256 PPU), and the board is never scaled - the camera
    // is fitted to it instead. Transform scale would leak into input maths and every fall distance.
    public sealed class BoardView : MonoBehaviour
    {
        public const float CellSize = 1f;

        // The four sprites one colour can show, in tier order. Serialized here rather than in a
        // ScriptableObject of its own: one theme, one consumer.
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

        [Header("Motion")]
        [Tooltip("Fall speed in cells per second. Every block moves at this rate, whatever the distance.")]
        [SerializeField] private float fallSpeed = 14f;

        [Tooltip("Seconds for the whole shuffle: blocks shrink away, the board is redrawn, they grow back.")]
        [SerializeField] private float shuffleDuration = 0.4f;

        private Board board;
        private BlockPool pool;
        private FallAnimator fallAnimator;

        // Holds moving blocks between detach and attach. Needed because moves chain: one block leaves
        // a cell in the same move another arrives on it.
        private BlockView[] movingBlocks;

        // Indexed exactly like the board's cells, so "which object is at cell i" never needs a search.
        private BlockView[] blockAt;

        // Centre of cell (0, 0) in world space. Row 0 is the bottom row, as everywhere else.
        private Vector3 origin;

        private float shuffleElapsed = NotShuffling;

        private bool shuffleRedrawn;

        private const float NotShuffling = -1f;

        private bool IsShuffling => shuffleElapsed >= 0f;

        // OnEnable/OnDisable and named methods, never Start/OnDestroy and never a lambda: an object
        // that is disabled and re-enabled would end up subscribed twice, and a lambda cannot be
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

        // Call once per board; Redraw afterwards for every change.
        public void Bind(Board newBoard)
        {
            board = newBoard ?? throw new ArgumentNullException(nameof(newBoard));

            ValidateSprites();

            origin = transform.position + new Vector3(
                -(board.Cols - 1) * 0.5f * CellSize,
                -(board.Rows - 1) * 0.5f * CellSize,
                0f);

            // Built once: a restart regenerates the same board object, so rebuilding here would
            // strand a board's worth of objects and allocate a second set.
            if (pool == null)
            {
                blockAt = new BlockView[board.CellCount];

                // No move can involve more blocks than the board has cells.
                movingBlocks = new BlockView[board.CellCount];

                fallAnimator = new FallAnimator(board.CellCount, fallSpeed);

                // Redraw returns every block before renting any, so the peak is exactly CellCount;
                // the spare row is headroom for a block held briefly by an effect.
                pool = new BlockPool(blockPrefab, transform, board.CellCount + board.Cols);
            }

            FitCamera();
        }

        // Full rebuild, not a diff: first draw and post-shuffle redraw only. Ordinary moves go
        // through ApplyBlast and touch just what changed.
        public void Redraw()
        {
            if (board == null) throw new InvalidOperationException("Redraw before Bind.");

            fallAnimator.Clear();

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
        public void ApplyBlast(BlastResult result)
        {
            if (board == null) throw new InvalidOperationException("ApplyBlast before Bind.");

            ReleaseBlocksAt(result.Removed);
            ReleaseBlocksAt(result.BrokenBoxes);

            ReadOnlySpan<int> from = result.MoveFrom;
            ReadOnlySpan<int> to = result.MoveTo;

            // Detach first, attach second: a block leaving cell 40 and another arriving on it belong
            // to the same move, so interleaving would hand one block to two cells.
            for (int i = 0; i < from.Length; i++)
            {
                int source = from[i];

                if (result.IsSpawn(source))
                {
                    // A spawn source decodes to a row above the board, which is where it should start.
                    BlockView spawned = pool.Rent();
                    spawned.Position = CellToWorld(source);

                    movingBlocks[i] = spawned;
                    continue;
                }

                // Still in the air from an earlier move? Cancelling leaves it where it is and the new
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
        }

        // Lives here because this class owns both halves of the mapping: the layout, and the animator
        // that knows which blocks have landed.
        // The settled filter asks about the tapped cell alone, never the group - landed blocks stay
        // tappable while others fall, so a group with one member mid-air must still blast.
        public bool TryPickCell(Vector3 screenPosition, out int cellIndex)
        {
            cellIndex = -1;
            if (board == null) return false;

            if (IsShuffling) return false;

            Vector3 world = boardCamera.ScreenToWorldPoint(screenPosition);

            // origin is the centre of cell (0,0), so half a cell shifts it to the lower-left corner.
            // Flooring, not a cast: a cast truncates towards zero and folds -0.4 onto cell 0.
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

            fallAnimator.Tick(Time.deltaTime);
            TickShuffleAnimation(Time.deltaTime);
        }

        // A shuffle moves no blocks, it swaps colour values, so without feedback the whole board would
        // change identity between two frames and read as a glitch. Shrink-and-grow carries the same
        // information as flying each colour to its cell, for a tenth of the work.
        // Scale is written per block, never on this transform: a scaled parent would stop one world
        // unit meaning one cell, and a child under a zero-scaled parent is a division by zero.
        private void BeginShuffleAnimation()
        {
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

            // 1 at both ends, 0 in the middle: one expression for shrink-then-grow, no phase flag.
            SetAllBlockScales(Mathf.Abs(1f - 2f * t));
        }

        private void SetAllBlockScales(float scale)
        {
            for (int i = 0; i < blockAt.Length; i++)
            {
                if (blockAt[i] == null) continue;

                blockAt[i].Scale = scale;
            }
        }

        private void ReleaseBlocksAt(ReadOnlySpan<int> cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                int cell = cells[i];
                if (blockAt[cell] == null) continue;

                fallAnimator.Cancel(cell);
                pool.Return(blockAt[cell]);
                blockAt[cell] = null;
            }
        }

        // Every block, not only the ones that moved: a group growing several columns away changes the
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

        // orthographicSize is the half-height in world units, so the width has to be divided by the
        // aspect ratio to be comparable. A 10x2 board is limited by width, a 2x10 board by height.
        private void FitCamera()
        {
            float verticalNeed = board.Rows * 0.5f * CellSize;
            float horizontalNeed = board.Cols * 0.5f * CellSize / boardCamera.aspect;

            boardCamera.orthographicSize = Mathf.Max(verticalNeed, horizontalNeed) + cameraPadding;

            // Centre on the board rather than requiring the board to sit at the world origin.
            Vector3 cameraPosition = transform.position;
            cameraPosition.z = boardCamera.transform.position.z;
            boardCamera.transform.position = cameraPosition;
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
