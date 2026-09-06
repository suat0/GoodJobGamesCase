using System;
using BlastGame.Core;
using UnityEngine;

namespace BlastGame.Game
{
    /// <summary>
    /// The Unity shell around a <see cref="GameSession"/>: builds the level from the config and announces
    /// what happened to whoever is listening.
    /// </summary>
    /// <remarks>
    /// <b>The one place LevelConfig meets Core.</b> Core is engine-free and cannot see a ScriptableObject,
    /// so the values are copied into a <see cref="BoardConfig"/> here and nowhere else.
    /// <para>
    /// <b>It holds no reference to anything that draws.</b> The view, the HUD and later the audio all
    /// subscribe to it; it subscribes to nothing. That keeps the dependency one-way - the harmful kind of
    /// coupling is the kind that points both ways (Karar 4) - and it is why this class compiles without
    /// knowing that a screen exists.
    /// </para>
    /// <para>
    /// <b>Named handlers, subscribed in OnEnable.</b> Listeners must follow the rules in Karar 4: hook up
    /// in <c>OnEnable</c> and unhook in <c>OnDisable</c>, never in Start/OnDestroy - an object that is
    /// disabled and re-enabled would otherwise end up subscribed twice and act on every event twice.
    /// </para>
    /// </remarks>
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] private LevelConfig level;

        /// <summary>A level is ready to be drawn from scratch. Raised at startup and after a restart.</summary>
        public event Action<Board> OnBoardReady;

        /// <summary>
        /// A move resolved. The argument is Core's single reused result - read it during the call, never
        /// store it (BlastResult).
        /// </summary>
        public event Action<BlastResult> OnBoardChanged;

        /// <summary>The board had no legal move and was rearranged. Listeners play the shuffle feedback.</summary>
        public event Action OnDeadlockResolved;

        /// <summary>Score, moves, remaining Boxes or the game state may have changed; re-read the session.</summary>
        public event Action OnStatusChanged;

        private GameSession session;

        /// <summary>Everything the HUD needs. Read-only to the rest of the game.</summary>
        public GameSession Session => session;

        // Start rather than Awake: every listener has subscribed by now, because Unity runs all of the
        // scene's Awake and OnEnable calls before the first Start. Raising OnBoardReady from Awake would
        // announce a board to an empty room.
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

            // Board makes a layout, GameSession plays one. The split is why the turn rules can be
            // tested on a stated board instead of a rolled one.
            var board = new Board(config, rng);
            board.Generate();

            session = new GameSession(board, level.MoveLimit);

            OnBoardReady?.Invoke(board);
            OnStatusChanged?.Invoke();
        }

        /// <summary>
        /// Plays a tap. Does nothing when the cell holds no blastable group or the level is over.
        /// </summary>
        /// <remarks>
        /// The whole turn - blast, damage, gravity, refill, the objective, the move limit and the deadlock
        /// check - resolves inside <see cref="GameSession.Play"/> before anything is announced, so no
        /// listener can ever see the board halfway through a move (Karar 5).
        /// </remarks>
        public void TryBlastAt(int cellIndex)
        {
            TurnResult turn = session.Play(cellIndex);
            if (!turn.Played) return;

            OnBoardChanged?.Invoke(session.Board.LastBlast);

            if (turn.Shuffled) OnDeadlockResolved?.Invoke();

            // Last, so listeners read a state that is finished settling.
            OnStatusChanged?.Invoke();
        }

        /// <summary>Starts the level over on a freshly generated board.</summary>
        /// <remarks>
        /// The same board object is regenerated rather than replaced, so the view keeps its pool and its
        /// arrays - a restart costs no allocation and no Instantiate.
        /// </remarks>
        public void Restart()
        {
            session.Restart();

            OnBoardReady?.Invoke(session.Board);
            OnStatusChanged?.Invoke();
        }
    }
}
