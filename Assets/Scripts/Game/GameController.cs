using System;
using BlastGame.Core;
using UnityEngine;

namespace BlastGame.Game
{
    // The Unity shell around a GameSession, and the one place LevelConfig meets Core - Core cannot see
    // a ScriptableObject, so the values are copied into a BoardConfig here and nowhere else.
    //
    // Holds no reference to anything that draws; the view and the HUD subscribe to it.
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] private LevelConfig level;

        public event Action<Board> OnBoardReady;

        // Carries Core's single reused result. Read it during the call, never store it.
        public event Action<BlastResult> OnBoardChanged;

        public event Action OnDeadlockResolved;

        public event Action OnStatusChanged;

        private GameSession session;

        public GameSession Session => session;

        // Start rather than Awake, so every listener has subscribed: Unity runs all of the scene's
        // Awake and OnEnable calls before the first Start.
        private void Start()
        {
            var config = new BoardConfig(
                level.Rows, level.Cols, level.ColorCount,
                level.ThresholdA, level.ThresholdB, level.ThresholdC,
                level.BoxCount);

            // Core is handed the Random itself, not the seed, so nothing inside it can reach for a
            // global generator and break reproducibility. Seed 0 means a different board every run.
            var rng = level.Seed == 0 ? new System.Random() : new System.Random(level.Seed);

            var board = new Board(config, rng);
            board.Generate();

            session = new GameSession(board, level.MoveLimit);

            OnBoardReady?.Invoke(board);
            OnStatusChanged?.Invoke();
        }

        public void TryBlastAt(int cellIndex)
        {
            TurnResult turn = session.Play(cellIndex);
            if (!turn.Played) return;

            OnBoardChanged?.Invoke(session.Board.LastBlast);

            if (turn.Shuffled) OnDeadlockResolved?.Invoke();

            OnStatusChanged?.Invoke();
        }

        // The board object is regenerated rather than replaced, so the view keeps its pool and arrays.
        public void Restart()
        {
            session.Restart();

            OnBoardReady?.Invoke(session.Board);
            OnStatusChanged?.Invoke();
        }
    }
}
