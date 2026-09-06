using BlastGame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace BlastGame.Game.UI
{
    /// <summary>
    /// Shows the score, the moves and the objective, and puts up the end-of-level panel.
    /// </summary>
    /// <remarks>
    /// <b>This is Canvas UI, and the board deliberately is not.</b> The reason the board avoids uGUI is
    /// the rebuild: changing a Graphic dirties its canvas, and a hundred blocks moving every frame would
    /// rebuild one every frame. Four labels that change once per move are the opposite case - the cost
    /// the board was protected from simply is not here, and hand-drawing text with sprites to avoid a
    /// cost that does not apply would be cargo cult (Karar 10).
    /// <para>
    /// The HUD sits on its own Canvas, so even that rebuild cannot reach anything else.
    /// </para>
    /// <para>
    /// <b>It reads the session rather than being told what to print.</b> The controller raises a bare
    /// "something changed" signal and this asks for what it needs, so a new field on the HUD costs
    /// nothing anywhere else.
    /// </para>
    /// </remarks>
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

        // Karar 4: OnEnable/OnDisable and named methods, never Start/OnDestroy and never a lambda - a
        // lambda cannot be unsubscribed, and one that captures allocates as well.
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

            // Interpolation allocates a string, once per move. Doing it per frame is what would matter,
            // and that is exactly what listening instead of polling avoids.
            scoreLabel.text = $"Score  {session.Score}";

            // With no objective there is nothing to run out of moves for, so the counter reports moves
            // made rather than moves left - the same label, told the truth that applies (Karar 7a).
            movesLabel.text = session.HasMoveLimit
                ? $"Moves  {session.MovesLeft}"
                : $"Moves  {session.Moves}";

            // The objective is data that can be absent, not a second game mode: the label is simply not
            // shown when the board generated without Boxes, as both of the case document's examples do.
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
