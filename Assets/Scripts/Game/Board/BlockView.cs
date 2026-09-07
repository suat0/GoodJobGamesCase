using UnityEngine;

namespace BlastGame.Game
{
    // A sprite at a position. The pool hands the same object to a different cell every few moves, so
    // anything stored here has to be reset on rent.
    //
    // Sorting layer and order live on the prefab and are never touched at runtime - half of what keeps
    // the board in a single draw call.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class BlockView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;

        // The fall animator writes a position per block per frame, and transform is a native lookup.
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

        // Z stays at 1. A sprite has no depth, and a zero there collapses the matrix, not the picture.
        public float Scale
        {
            get => cachedTransform.localScale.x;
            set => cachedTransform.localScale = new Vector3(value, value, 1f);
        }

        // A method rather than a second property, so nobody mistakes it for the common case.
        public void SetScale(float x, float y) => cachedTransform.localScale = new Vector3(x, y, 1f);

        // Degrees about z; a sprite in the xy plane has no other axis worth turning.
        public float Rotation
        {
            get => cachedTransform.localEulerAngles.z;
            set => cachedTransform.localRotation = Quaternion.Euler(0f, 0f, value);
        }

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
