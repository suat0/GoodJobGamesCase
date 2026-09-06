using System;
using BlastGame.Core;
using UnityEngine;

namespace BlastGame.Game
{
    /// <summary>
    /// Draws a <see cref="Board"/>: one pooled sprite per non-empty cell, laid out on a one-unit grid.
    /// </summary>
    /// <remarks>
    /// <b>Reads Core, never writes it.</b> The board is passed in and only queried; every decision this
    /// class makes is about pixels. That is the boundary from Karar 1, and it is what lets the animation
    /// added in the next phase be purely cosmetic.
    /// <para>
    /// <b>One world unit per cell.</b> The sprites are 256px at 256 pixels-per-unit, so a cell is exactly
    /// one unit and the board is never scaled - the camera is fitted to the board instead. Scale that
    /// lives in a transform leaks into input maths, fall distances and every position calculation;
    /// keeping it out of them entirely costs nothing (DECISIONS.md, Karar 10).
    /// </para>
    /// </remarks>
    public sealed class BoardView : MonoBehaviour
    {
        /// <summary>Cell size in world units. Not configurable on purpose - see the class remarks.</summary>
        public const float CellSize = 1f;

        /// <summary>
        /// The four sprites one colour can show, in tier order. A serialized array on the view rather
        /// than a ScriptableObject of its own: there is one theme and one consumer, so a second asset
        /// would add a file to open without removing a decision from anywhere.
        /// </summary>
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

        private Board board;
        private BlockPool pool;
        private FallAnimator fallAnimator;

        /// <summary>
        /// Holds the moving blocks between detaching them from their source cells and attaching them to
        /// their targets. Needed because moves chain - one block leaves a cell in the same move another
        /// arrives on it - so every source has to be read before any destination is written.
        /// </summary>
        private BlockView[] movingBlocks;

        /// <summary>
        /// The block drawn at each cell, or null where the cell is empty. Indexed exactly like the
        /// board's own cells, so "which object is at cell i" never needs a search.
        /// </summary>
        private BlockView[] blockAt;

        /// <summary>Centre of cell (0, 0) in world space. Row 0 is the bottom row, as everywhere else.</summary>
        private Vector3 origin;


        // Karar 4: subscribe in OnEnable, unsubscribe in OnDisable, always through named methods. Start
        // and OnDestroy would leave an object that is disabled and re-enabled subscribed twice, and a
        // lambda could never be unsubscribed at all - it also captures, which allocates.
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

        private void HandleDeadlockResolved() => Redraw();

        /// <summary>
        /// Attaches the view to a board: builds the pool, sizes the internal arrays and frames the camera.
        /// Call once per board; <see cref="Redraw"/> afterwards for every change.
        /// </summary>
        public void Bind(Board newBoard)
        {
            board = newBoard ?? throw new ArgumentNullException(nameof(newBoard));

            ValidateSprites();

            origin = transform.position + new Vector3(
                -(board.Cols - 1) * 0.5f * CellSize,
                -(board.Rows - 1) * 0.5f * CellSize,
                0f);

            // Built once. A restart regenerates the same board object rather than replacing it, so
            // rebuilding here would strand a whole board's worth of objects in the scene and allocate a
            // second set - a restart has to cost nothing.
            if (pool == null)
            {
                blockAt = new BlockView[board.CellCount];

                // No move can involve more blocks than the board has cells: every move is identified by
                // the cell it lands on, and no two land on the same one (BlastResult).
                movingBlocks = new BlockView[board.CellCount];

                fallAnimator = new FallAnimator(board.CellCount, fallSpeed);

                // The board can never show more blocks than it has cells, and Redraw returns every block
                // before it rents any, so the peak is exactly CellCount. The spare row is headroom for a
                // block held briefly by an effect later, and it makes an off-by-one impossible rather
                // than merely unlikely.
                pool = new BlockPool(blockPrefab, transform, board.CellCount + board.Cols);
            }

            FitCamera();
        }

        /// <summary>
        /// Rebuilds the whole board from Core's current state.
        /// </summary>
        /// <remarks>
        /// A full rebuild, not a diff. The board is static in this phase, so this runs once; the move
        /// animation added next reads <see cref="BlastResult"/> and touches only what changed, and this
        /// stays as the way a board is first shown and the way a shuffle is redrawn.
        /// </remarks>
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
                if (sprite == null) continue;          // an empty cell: a hole under a Box, and nothing to draw

                BlockView block = pool.Rent();
                block.Sprite = sprite;
                block.Position = CellToWorld(i);

                blockAt[i] = block;
            }
        }

        /// <summary>
        /// Catches the board up with a move Core has already resolved: blasted blocks go back to the
        /// pool, survivors start falling to their new cells, new blocks enter from above the board.
        /// </summary>
        /// <remarks>
        /// Everything in <paramref name="result"/> is read during this call and never kept - Core reuses
        /// a single instance, so a stored reference would show the next move's data (BlastResult).
        /// </remarks>
        public void ApplyBlast(BlastResult result)
        {
            if (board == null) throw new InvalidOperationException("ApplyBlast before Bind.");

            ReleaseBlocksAt(result.Removed);
            ReleaseBlocksAt(result.BrokenBoxes);

            ReadOnlySpan<int> from = result.MoveFrom;
            ReadOnlySpan<int> to = result.MoveTo;

            // Detach first, attach second. A block leaving cell 40 and another arriving on it belong to
            // the same move, so writing destinations while sources are still being read would hand one
            // block to two cells.
            for (int i = 0; i < from.Length; i++)
            {
                int source = from[i];

                if (result.IsSpawn(source))
                {
                    // A spawn source decodes to a row above the top of the board, which is exactly where
                    // the block should start - so new blocks need no separate rule, only a rent.
                    BlockView spawned = pool.Rent();
                    spawned.Position = CellToWorld(source);

                    movingBlocks[i] = spawned;
                    continue;
                }

                // The block may still be in the air from an earlier move. Cancelling leaves it exactly
                // where it is and the new fall starts from there, so a fast player sees a redirection
                // rather than a jump.
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

        /// <summary>
        /// Converts a screen point to the cell that was tapped, or false when the tap should be ignored.
        /// </summary>
        /// <remarks>
        /// Lives here because this class already owns both halves of the mapping: the layout that turns a
        /// cell into a position, and the animator that knows which blocks have landed.
        /// <para>
        /// <b>The settled filter is the last step, and it is the only one.</b> It asks about the tapped
        /// cell alone, never about the group - the case document requires that blocks which have landed
        /// stay tappable while others are still falling, so a group with one member mid-air must still
        /// blast (Karar 5, B2).
        /// </para>
        /// </remarks>
        public bool TryPickCell(Vector3 screenPosition, out int cellIndex)
        {
            cellIndex = -1;
            if (board == null) return false;

            Vector3 world = boardCamera.ScreenToWorldPoint(screenPosition);

            // origin is the centre of cell (0,0), so half a cell shifts it to that cell's lower-left
            // corner and the floor lands on the right square. Flooring is what makes negative
            // coordinates work: a cast to int truncates towards zero and would fold -0.4 onto cell 0.
            int col = Mathf.FloorToInt((world.x - origin.x) / CellSize + 0.5f);
            int row = Mathf.FloorToInt((world.y - origin.y) / CellSize + 0.5f);

            if (row < 0 || row >= board.Rows || col < 0 || col >= board.Cols) return false;

            int candidate = row * board.Cols + col;
            if (!fallAnimator.IsSettled(candidate)) return false;

            cellIndex = candidate;
            return true;
        }

        /// <summary>
        /// The single per-frame loop for the whole board. No block has an Update of its own.
        /// </summary>
        private void Update()
        {
            if (fallAnimator == null) return;   // before Bind

            fallAnimator.Tick(Time.deltaTime);
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

        /// <summary>
        /// Re-reads every block's sprite from Core.
        /// </summary>
        /// <remarks>
        /// Every block, not only the ones that moved. A blast merges and splits groups, and a group that
        /// grows several columns away changes the icon tier of blocks that did not move a single cell -
        /// so "what changed" is not a question the move list can answer. A hundred sprite assignments
        /// per move is not worth the bug of getting that wrong.
        /// <para>
        /// Damaged Boxes need no case of their own: their health changed, so the same lookup already
        /// returns the cracked sprite.
        /// </para>
        /// </remarks>
        private void RefreshSprites()
        {
            for (int i = 0; i < blockAt.Length; i++)
            {
                if (blockAt[i] == null) continue;

                blockAt[i].Sprite = SpriteFor(i);
            }
        }

        /// <summary>World position of a cell's centre. Valid for rows above the board, which is where new blocks start.</summary>
        public Vector3 CellToWorld(int index)
        {
            int row = index / board.Cols;
            int col = index - row * board.Cols;

            return origin + new Vector3(col * CellSize, row * CellSize, 0f);
        }

        /// <summary>
        /// Which sprite a cell shows, or null when it shows nothing.
        /// </summary>
        /// <remarks>
        /// The icon tier is asked of Core per cell rather than stored here. It is three comparisons over
        /// data Core already has, and a copy kept on this side would be a second version of the truth
        /// that goes stale silently - as a wrong sprite, which nobody notices by looking (Karar 14).
        /// </remarks>
        private Sprite SpriteFor(int index)
        {
            Cell cell = board.CellAt(index);

            if (cell.IsBox) return boxSprites[Cell.BoxMaxHealth - cell.Health];
            if (!cell.IsColor) return null;

            return colorSprites[cell.Color].ForTier(board.TierAt(index));
        }

        /// <summary>
        /// Fits the board to the screen by widening the camera, never by scaling the board.
        /// </summary>
        /// <remarks>
        /// <c>orthographicSize</c> is the half-height in world units, so the height needs half the rows;
        /// the width has to be divided by the aspect ratio to be expressed in the same terms. Whichever
        /// of the two is larger is the one that would otherwise be cut off. A 10x2 board is limited by
        /// its width, a 2x10 board by its height, and the same line handles both.
        /// </remarks>
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

        /// <summary>
        /// Checks the inspector wiring once, at bind time, with messages that name what is missing.
        /// </summary>
        /// <remarks>
        /// An unassigned sprite otherwise surfaces as an invisible block or a null reference thrown from
        /// inside the draw loop, a hundred cells into a frame, with nothing in the message about which
        /// colour was never wired up.
        /// </remarks>
        private void ValidateSprites()
        {
            if (blockPrefab == null) throw new InvalidOperationException($"{name}: block prefab is not assigned.");
            if (boardCamera == null) throw new InvalidOperationException($"{name}: board camera is not assigned.");

            if (boxSprites == null || boxSprites.Length != Cell.BoxMaxHealth)
                throw new InvalidOperationException(
                    $"{name}: needs exactly {Cell.BoxMaxHealth} Box sprites, one per damage level.");

            // The board is the authority on how many colours are actually in play: LevelConfig's
            // ColorCount never reaches this side, and a level that uses four colours must not be
            // rejected for having six sprite slots - or accepted with only three filled.
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
