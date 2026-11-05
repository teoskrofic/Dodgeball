using UnityEngine;

namespace Dodgeball
{
    /// <summary>
    /// Small positional camera shake. Call CameraShake.Shake(strength, duration) from anywhere.
    /// </summary>
    public class CameraShake : MonoBehaviour
    {
        private static CameraShake instance;

        private Vector3 restPosition;
        private float strength;
        private float timeLeft;
        private float duration;

        private void Awake()
        {
            instance = this;
            restPosition = transform.localPosition;
        }

        public static void Shake(float shakeStrength, float shakeDuration)
        {
            if (instance == null) return;
            instance.strength = Mathf.Max(instance.strength * (instance.timeLeft > 0f ? 1f : 0f), shakeStrength);
            instance.duration = shakeDuration;
            instance.timeLeft = shakeDuration;
        }

        private void LateUpdate()
        {
            if (timeLeft <= 0f) return;
            timeLeft -= Time.unscaledDeltaTime;
            float fade = Mathf.Clamp01(timeLeft / Mathf.Max(duration, 0.0001f));
            transform.localPosition = timeLeft > 0f ? restPosition + Random.insideUnitSphere * strength * fade : restPosition;
        }
    }
}
