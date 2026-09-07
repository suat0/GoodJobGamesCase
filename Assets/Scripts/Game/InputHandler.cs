using UnityEngine;

namespace BlastGame.Game
{
    // Turns a click into a cell with arithmetic - no colliders on the board. The reason is
    // correctness, not speed: a raycast asks the VISUAL world what was hit, and during a fall the
    // visual world is deliberately behind the logical one.
    // Calls the controller directly - a tap has exactly one consumer, so an event would be pattern
    // without reason.
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
