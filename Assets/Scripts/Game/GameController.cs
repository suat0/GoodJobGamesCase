using System;
using BlastGame.Core;
using UnityEngine;

namespace BlastGame.Game
{
    // The Unity shell around a GameSession, and the one place LevelConfig meets Core: Core cannot see
    // a ScriptableObject, so the values are copied into a BoardConfig here and nowhere else.
    // Holds no reference to anything that draws - the view and the HUD subscribe to it, not the
    // other way round.
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] private LevelConfig level;

        // Raised at startup and after a restart.
        public event Action<Board> OnBoardReady;

        // Core's single reused result - read it during the call, never store it.
        public event Action<BlastResult> OnBoardChanged;

        public event Action OnDeadlockResolved;

        // Score, moves, remaining Boxes or the game state may have changed; re-read the session.
        public event Action OnStatusChanged;

        private GameSession session;

        public GameSession Session => session;

        // Start, not Awake: every listener has subscribed by now, because Unity runs all of the
        // scene's Awake and OnEnable calls before the first Start.
        private void Start()
        {
            var config = new BoardConfig(
                level.Rows, level.Cols, level.ColorCount,
                level.ThresholdA, level.ThresholdB, level.ThresholdC,
                level.BoxCount);

            // Core takes the Random, not the seed, so nothing inside it can reach for a global
            // generator and break reproducibility. Seed 0 means a different board every run.
            var rng = level.Seed == 0 ? new System.Random() : new System.Random(level.Seed);

            // Board makes a layout, GameSession plays one.
            var board = new Board(config, rng);
            board.Generate();

            session = new GameSession(board, level.MoveLimit);

            OnBoardReady?.Invoke(board);
            OnStatusChanged?.Invoke();
        }

        public void TryBlastAt(int cellIndex)
        {
            // The whole turn resolves inside Play before anything is announced, so no listener can see
            // the board halfway through a move.
            TurnResult turn = session.Play(cellIndex);
            if (!turn.Played) return;

            OnBoardChanged?.Invoke(session.Board.LastBlast);

            if (turn.Shuffled) OnDeadlockResolved?.Invoke();

            // Last, so listeners read a state that is finished settling.
            OnStatusChanged?.Invoke();
        }

        // The same board object is regenerated, not replaced, so the view keeps its pool and arrays.
        public void Restart()
        {
            session.Restart();

            OnBoardReady?.Invoke(session.Board);
            OnStatusChanged?.Invoke();
        }
    }
}
