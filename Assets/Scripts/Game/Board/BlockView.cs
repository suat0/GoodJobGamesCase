using UnityEngine;

namespace BlastGame.Game
{
    // A sprite at a position, and nothing else. The pool hands the same object to a different cell
    // every few moves, so any state kept here would have to be reset on every rent.
    // Sorting layer and order live on the prefab and are never touched at runtime - half of what keeps
    // the board in a single draw call.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class BlockView : MonoBehaviour
    {
        // Wired in the prefab, not fetched: the pool builds a board's worth of these in one frame.
        [SerializeField] private SpriteRenderer spriteRenderer;

        // transform is a native lookup, and the fall animator writes a position per block per frame.
        private Transform cachedTransform;

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

        // Z stays at 1: a sprite has no depth, and a zero there collapses the matrix rather than the
        // picture.
        public float Scale
        {
            get => cachedTransform.localScale.x;
            set => cachedTransform.localScale = new Vector3(value, value, 1f);
        }

        // Squash needs the axes to differ, which the uniform setter above cannot express. Kept as a
        // method rather than a second property so no caller mistakes it for the common case.
        public void SetScale(float x, float y) => cachedTransform.localScale = new Vector3(x, y, 1f);

        // Degrees about z. A sprite lying in the xy plane has no other axis worth turning, and taking
        // a float instead of a Quaternion keeps the effect data a plain number to integrate.
        public float Rotation
        {
            get => cachedTransform.localEulerAngles.z;
            set => cachedTransform.localRotation = Quaternion.Euler(0f, 0f, value);
        }

        // Tint and alpha in one, because SpriteRenderer stores them in one. Effects only ever fade,
        // but exposing the whole colour costs nothing and keeps the reset in the pool a single write.
        public Color Color
        {
            get => spriteRenderer.color;
            set => spriteRenderer.color = value;
        }

        public float Alpha
        {
            get => spriteRenderer.color.a;
            set
            {
                Color c = spriteRenderer.color;
                c.a = value;
                spriteRenderer.color = c;
            }
        }

#if UNITY_EDITOR
        private void Reset() => spriteRenderer = GetComponent<SpriteRenderer>();
#endif
    }
}
