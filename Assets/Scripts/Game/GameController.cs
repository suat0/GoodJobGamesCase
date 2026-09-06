using BlastGame.Core;
using UnityEngine;

namespace BlastGame.Game
{
    /// <summary>
    /// Owns the board and starts the level. The move flow, scoring and win/lose states arrive here later.
    /// </summary>
    /// <remarks>
    /// <b>The one place LevelConfig meets Core.</b> Core is engine-free and cannot see a ScriptableObject,
    /// so the values are copied into a <see cref="BoardConfig"/> here and nowhere else - a new setting has
    /// exactly one place to be threaded through (CLAUDE.md, data model).
    /// <para>
    /// The board is created here rather than by the view, because the view's job is to draw a board, not
    /// to decide which board exists. Keeping ownership on this side is what lets the view stay a pure
    /// reader of Core.
    /// </para>
    /// </remarks>
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] private LevelConfig level;
        [SerializeField] private BoardView boardView;

        private Board board;

        private void Start()
        {
            var config = new BoardConfig(
                level.Rows, level.Cols, level.ColorCount,
                level.ThresholdA, level.ThresholdB, level.ThresholdC,
                level.BoxCount);

            // Seed 0 means "a different board every run"; anything else reproduces one exactly. Core takes
            // the Random rather than the seed, so nothing inside it can reach for a global generator and
            // quietly break that reproducibility (CLAUDE.md, architecture rule 1).
            var rng = level.Seed == 0 ? new System.Random() : new System.Random(level.Seed);

            board = new Board(config, rng);
            board.Generate();

            // A freshly generated board can be born without a legal move. The same two calls that handle
            // a deadlock mid-game handle it here, so the opening needs no guarantee of its own and the
            // shuffle path is not code that only runs on a rare board (DECISIONS.md, Karar 16).
            if (board.IsDeadlocked) board.TryResolveDeadlock();

            boardView.Bind(board);
            boardView.Redraw();
        }
    }
}
