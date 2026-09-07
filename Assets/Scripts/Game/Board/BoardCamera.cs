using System;
using UnityEngine;

namespace BlastGame.Game
{
    // Frames the board and knocks the view when something breaks. Split out of BoardView because it
    // owns the position the shake departs from, and because framing a board and drawing one are
    // separate jobs that happened to share a class.
    public sealed class BoardCamera
    {
        private readonly Camera camera;

        private readonly float padding;          // world units of empty space around the board
        private readonly float shakeMagnitude;
        private readonly float shakeDuration;

        // Where Frame put the camera. Held apart from the camera's own position, which the shake
        // overwrites every frame - reading that back would let each shake start from the last one's
        // offset and walk the view off the board.
        private Vector3 basePosition;

        private float shakeElapsed = NotShaking;

        private float shakeStrength;

        private const float NotShaking = -1f;

        public BoardCamera(Camera camera, float padding, float shakeMagnitude, float shakeDuration)
        {
            this.camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));

            if (shakeDuration <= 0f)
                throw new ArgumentOutOfRangeException(nameof(shakeDuration), shakeDuration, "Duration must be positive.");

            this.padding = padding;
            this.shakeMagnitude = shakeMagnitude;
            this.shakeDuration = shakeDuration;
        }

        // orthographicSize is the half-height in world units, so the width has to be divided by the
        // aspect ratio to be comparable. A 10x2 board is limited by width, a 2x10 board by height.
        public void Frame(int rows, int cols, float cellSize, Vector3 center)
        {
            // The padding joins each need before the comparison. Added to the result instead it would
            // be half-height either way, which on a portrait screen shrinks to a fraction of itself
            // horizontally, and a wide board would touch both edges.
            float verticalNeed = rows * 0.5f * cellSize + padding;
            float horizontalNeed = (cols * 0.5f * cellSize + padding) / camera.aspect;

            camera.orthographicSize = Mathf.Max(verticalNeed, horizontalNeed);

            center.z = camera.transform.position.z;

            basePosition = center;
            camera.transform.position = center;
        }

        // Restarted rather than stacked, so a chain of breaks reads as one knock.
        public void Shake(float strength)
        {
            shakeElapsed = 0f;
            shakeStrength = strength;
        }

        // Two sine waves at unrelated rates. A random offset per frame reads as video noise at 60fps;
        // this costs nothing and always ends where it started.
        public void Tick(float deltaTime)
        {
            if (shakeElapsed < 0f) return;

            shakeElapsed += deltaTime;
            float t = shakeElapsed / shakeDuration;

            if (t >= 1f)
            {
                shakeElapsed = NotShaking;
                camera.transform.position = basePosition;
                return;
            }

            const float Frequency = 42f;

            float amplitude = shakeMagnitude * shakeStrength * (1f - t);

            camera.transform.position = basePosition + new Vector3(
                Mathf.Sin(shakeElapsed * Frequency) * amplitude,
                Mathf.Cos(shakeElapsed * Frequency * 1.37f) * amplitude * 0.6f,
                0f);
        }
    }
}
