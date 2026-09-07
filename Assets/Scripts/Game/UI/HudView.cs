using BlastGame.Core;
using TMPro;
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
        [SerializeField] private TMP_Text scoreLabel;
        [SerializeField] private TMP_Text movesLabel;
        [SerializeField] private TMP_Text objectiveLabel;

        [Tooltip("Hidden whole when the level has no objective, so the icon goes with the count.")]
        [SerializeField] private GameObject objectiveGroup;

        [Header("End of level")]
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private CanvasGroup gameOverDim;
        [SerializeField] private RectTransform gameOverCard;
        [SerializeField] private TMP_Text gameOverLabel;
        [SerializeField] private Button restartButton;

        [Header("Motion")]
        [Tooltip("Points per second the displayed score climbs. A score that snaps gives the player " +
                 "nothing to read; one that crawls makes them wait.")]
        [SerializeField] private float scoreCountRate = 1400f;

        [SerializeField] private float scorePunch = 0.22f;
        [SerializeField] private float scorePunchDuration = 0.22f;
        [SerializeField] private float popupDuration = 0.34f;

        private int targetScore;

        // Kept as a float so a rate in points per second survives a frame shorter than one point.
        private float shownScore;

        private float punchElapsed = Idle;
        private float popupElapsed = Idle;

        private const float Idle = -1f;

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

            // The score is the one number that animates, so it is the one the label does not read
            // directly: Tick walks the displayed value up to this.
            if (session.Score > targetScore) punchElapsed = 0f;
            else shownScore = session.Score;          // a restart drops it back, with nothing to count

            targetScore = session.Score;

            // SetText with an argument, not an interpolated string: TMP formats into a buffer it
            // already owns, so a label that changes every frame while the score climbs allocates
            // nothing. String interpolation here would put a few hundred bytes a second on the heap.
            movesLabel.SetText("{0:0}", session.HasMoveLimit ? session.MovesLeft : session.Moves);

            // The objective is data that can be absent, not a second game mode.
            objectiveGroup.SetActive(session.HasObjective);
            if (session.HasObjective) objectiveLabel.SetText("{0:0}", session.RemainingBoxes);

            ShowOutcome(session.State);
        }

        // Runs every frame but does nothing on almost all of them: two float comparisons against a
        // sentinel. Enabling and disabling the component instead would cost more than it saved.
        private void Update()
        {
            float deltaTime = Time.deltaTime;

            TickScore(deltaTime);
            TickPunch(deltaTime);
            TickPopup(deltaTime);
        }

        private void TickScore(float deltaTime)
        {
            if (shownScore == targetScore) return;

            shownScore = Mathf.MoveTowards(shownScore, targetScore, scoreCountRate * deltaTime);

            // Ceil, not round: the counter should reach the real score on its last frame rather than
            // arriving a point short and correcting.
            scoreLabel.SetText("{0:0}", Mathf.Ceil(shownScore));
        }

        // Up and back down on one sine, so the label always ends at exactly its own size.
        private void TickPunch(float deltaTime)
        {
            if (punchElapsed < 0f) return;

            punchElapsed += deltaTime;
            float t = punchElapsed / scorePunchDuration;

            if (t >= 1f)
            {
                punchElapsed = Idle;
                scoreLabel.rectTransform.localScale = Vector3.one;
                return;
            }

            float scale = 1f + scorePunch * Mathf.Sin(t * Mathf.PI);
            scoreLabel.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        private void TickPopup(float deltaTime)
        {
            if (popupElapsed < 0f) return;

            popupElapsed += deltaTime;
            float t = Mathf.Clamp01(popupElapsed / popupDuration);

            gameOverDim.alpha = t;

            // Written straight onto the scale rather than through a Lerp: OutBack overshoots past 1,
            // and Mathf.Lerp would clamp the overshoot away.
            float scale = Easing.OutBack(t);
            gameOverCard.localScale = new Vector3(scale, scale, 1f);

            if (t >= 1f) popupElapsed = Idle;
        }

        private void ShowOutcome(GameState state)
        {
            bool over = state != GameState.Playing;

            // Only on the transition: re-running the entrance every time the status changes would
            // restart the animation while the player is looking at it.
            if (over && !gameOverPanel.activeSelf)
            {
                popupElapsed = 0f;
                gameOverDim.alpha = 0f;
                gameOverCard.localScale = Vector3.zero;
            }

            gameOverPanel.SetActive(over);

            if (!over) return;

            gameOverLabel.text = state == GameState.Won ? "Level complete" : "Out of moves";
        }

        private void HandleRestartClicked() => controller.Restart();
    }
}
