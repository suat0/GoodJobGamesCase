using BlastGame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace BlastGame.Game.UI
{
    // Score, moves, objective, and the end-of-level panel.
    // Canvas UI here, deliberately not on the board: what the board avoids is the canvas rebuild, and
    // four labels changing once per move are the opposite of a hundred blocks moving every frame.
    // Reads the session rather than being told what to print, so a new label costs nothing elsewhere.
    public sealed class HudView : MonoBehaviour
    {
        [SerializeField] private GameController controller;

        [Header("Labels")]
        [SerializeField] private Text scoreLabel;
        [SerializeField] private Text movesLabel;
        [SerializeField] private Text objectiveLabel;

        [Header("Game over")]
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private Text gameOverLabel;
        [SerializeField] private Button restartButton;

        private void OnEnable()
        {
            controller.OnStatusChanged += HandleStatusChanged;
            restartButton.onClick.AddListener(HandleRestartClicked);
        }

        private void OnDisable()
        {
            controller.OnStatusChanged -= HandleStatusChanged;
            restartButton.onClick.RemoveListener(HandleRestartClicked);
        }

        private void HandleStatusChanged()
        {
            GameSession session = controller.Session;

            // One string per move. Doing it per frame is what would matter, and listening instead of
            // polling is what avoids that.
            scoreLabel.text = $"Score  {session.Score}";

            // With no objective there is nothing to run out of moves for: report moves made instead.
            movesLabel.text = session.HasMoveLimit
                ? $"Moves  {session.MovesLeft}"
                : $"Moves  {session.Moves}";

            // The objective is data that can be absent, not a second game mode.
            objectiveLabel.gameObject.SetActive(session.HasObjective);
            if (session.HasObjective) objectiveLabel.text = $"Boxes  {session.RemainingBoxes}";

            ShowOutcome(session.State);
        }

        private void ShowOutcome(GameState state)
        {
            gameOverPanel.SetActive(state != GameState.Playing);

            if (state == GameState.Playing) return;

            gameOverLabel.text = state == GameState.Won ? "Level complete" : "Out of moves";
        }

        private void HandleRestartClicked() => controller.Restart();
    }
}
