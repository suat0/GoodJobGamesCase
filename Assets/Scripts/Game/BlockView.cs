using UnityEngine;

namespace BlastGame.Game
{
    /// <summary>
    /// One block on the board: a sprite at a position, and nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately dumb. It holds no cell index, no colour and no idea of the board, because the pool
    /// hands the same object to a different cell every few moves - any state kept here would be state
    /// that has to be reset on every rent, and forgetting one field is a bug that renders as a wrong
    /// sprite rather than as an error.
    /// <para>
    /// Sorting layer and order live on the prefab and are never touched at runtime: every block shares
    /// them, which is half of what keeps the board in a single draw call (DECISIONS.md, Karar 10).
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class BlockView : MonoBehaviour
    {
        /// <summary>
        /// Wired in the prefab rather than fetched. <c>GetComponent</c> in Awake would also run once per
        /// block, but the pool builds a whole board's worth of them in one frame at startup - the frame
        /// that should be doing the least avoidable work.
        /// </summary>
        [SerializeField] private SpriteRenderer spriteRenderer;

        /// <summary>
        /// <c>transform</c> is a native lookup on every access, and the fall animator writes a position
        /// per falling block per frame.
        /// </summary>
        private Transform cachedTransform;

        // Runs on Instantiate, and the pool creates its blocks active for exactly that reason.
        private void Awake() => cachedTransform = transform;

        public Sprite Sprite
        {
            get => spriteRenderer.sprite;
            set => spriteRenderer.sprite = value;
        }

        public Vector3 Position
        {
            get => cachedTransform.position;
            set => cachedTransform.position = value;
        }

        /// <summary>
        /// Uniform cosmetic scale, used by the shuffle feedback. Z is left at 1 - a sprite has no depth,
        /// and a zero there would collapse the matrix rather than the picture.
        /// </summary>
        public float Scale
        {
            get => cachedTransform.localScale.x;
            set => cachedTransform.localScale = new Vector3(value, value, 1f);
        }

#if UNITY_EDITOR
        // Fills the reference when the component is first added, so the prefab cannot be authored with
        // it left empty.
        private void Reset() => spriteRenderer = GetComponent<SpriteRenderer>();
#endif
    }
}
