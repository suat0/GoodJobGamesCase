using UnityEngine;

namespace BlastGame.Game
{
    // The curves the view animates along, named and in one place. Pure float maths: no state, no
    // allocation, nothing to tick.
    //
    // Deliberately only the curves that have a caller. An easing table is where a dozen unused
    // functions accumulate, and a reader cannot tell an unused curve from a curve used somewhere
    // they have not read yet.
    //
    // Each takes and returns normalised time. Where a curve leaves 0..1 - OutBack does, by design -
    // the caller must use LerpUnclamped: Mathf.Lerp clamps its own t and would swallow the overshoot
    // silently, leaving an animation that looks merely linear rather than obviously broken.
    public static class Easing
    {
        // Slow to leave, fast to arrive. Read backwards, InQuad(1 - t), it is a decay that starts at
        // full strength and eases to nothing - which is what a recovering squash wants.
        public static float InQuad(float t) => t * t;

        // Eases at both ends and is symmetric, so a shrink and the grow that mirrors it can share it.
        public static float SmoothStep(float t) => t * t * (3f - 2f * t);

        // Overshoots past 1 and settles back. The two constants are Penner's: c1 is the overshoot
        // amount and c3 is what keeps the curve passing through 1 at t = 1.
        public static float OutBack(float t)
        {
            const float C1 = 1.70158f;
            const float C3 = C1 + 1f;

            float u = t - 1f;
            return 1f + C3 * u * u * u + C1 * u * u;
        }
    }
}
