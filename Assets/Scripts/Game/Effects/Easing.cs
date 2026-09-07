namespace BlastGame.Game
{
    // Curves the view animates along. Pure float maths, no state.
    //
    // OutBack leaves 0..1 on purpose, so callers must use LerpUnclamped - Mathf.Lerp clamps its own t
    // and swallows the overshoot silently, leaving an animation that just looks linear.
    public static class Easing
    {
        public static float InQuad(float t) => t * t;

        // Symmetric, so a shrink and the grow that mirrors it can share it.
        public static float SmoothStep(float t) => t * t * (3f - 2f * t);

        // Overshoots past 1 and settles back. Penner's constants.
        public static float OutBack(float t)
        {
            const float C1 = 1.70158f;
            const float C3 = C1 + 1f;

            float u = t - 1f;
            return 1f + C3 * u * u * u + C1 * u * u;
        }
    }
}
