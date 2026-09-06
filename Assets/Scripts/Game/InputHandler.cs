using UnityEngine;

namespace BlastGame.Game
{
    /// <summary>
    /// Turns a click into a cell and hands it to the game.
    /// </summary>
    /// <remarks>
    /// <b>No colliders anywhere on the board.</b> The screen point is converted with arithmetic rather
    /// than a raycast. The reason is correctness before performance: a raycast asks the <i>visual</i>
    /// world which object was hit, and during a fall the visual world is deliberately behind the logical
    /// one - it would return the cell a block is passing through instead of the cell that was tapped,
    /// and that breaks the settled-block rule rather than merely being slower. The physics engine never
    /// waking up is a pleasant side effect (DECISIONS.md, Karar 11).
    /// <para>
    /// <b>The old Input API.</b> The new Input System needs a package, an action asset and a component
    /// to answer "was there a click, and where". One line against that setup cost is not a close call;
    /// <c>GetMouseButtonDown</c> also reports a single touch on mobile, so nothing is given up.
    /// </para>
    /// <para>
    /// Calls the controller directly instead of raising an event. The Observer setup in Karar 4 was
    /// justified by having several planned listeners for board changes; a tap has exactly one consumer,
    /// and an event here would be the pattern without the reason.
    /// </para>
    /// </remarks>
    public sealed class InputHandler : MonoBehaviour
    {
        [SerializeField] private BoardView boardView;
        [SerializeField] private GameController controller;

        private void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return;

            // Swallows taps outside the board and taps on blocks that are still falling.
            if (!boardView.TryPickCell(Input.mousePosition, out int cellIndex)) return;

            controller.TryBlastAt(cellIndex);
        }
    }
}
