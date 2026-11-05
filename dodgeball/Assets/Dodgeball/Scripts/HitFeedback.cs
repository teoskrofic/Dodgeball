using System.Collections;
using UnityEngine;

namespace Dodgeball
{
    /// <summary>
    /// Flashes the character's renderers, shakes the camera and plays a sound when Health is damaged.
    /// Also makes the character blink while it is invulnerable (dodging).
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class HitFeedback : MonoBehaviour
    {
        [SerializeField] private Color flashColor = Color.white;
        [SerializeField] private float flashDuration = 0.15f;
        [SerializeField] private int flashCount = 3;
        [SerializeField] private float shakeStrength = 0.35f;
        [SerializeField] private float shakeDuration = 0.25f;
        [SerializeField] private AudioClip hitSound;

        private Health health;
        private Renderer[] renderers;
        private MaterialPropertyBlock block;

        private void Awake()
        {
            health = GetComponent<Health>();
            renderers = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
        }

        private void OnEnable() => health.OnDamaged += HandleDamaged;
        private void OnDisable() => health.OnDamaged -= HandleDamaged;

        private void HandleDamaged(int livesLeft)
        {
            StopAllCoroutines();
            StartCoroutine(Flash());
            CameraShake.Shake(shakeStrength, shakeDuration);
            if (hitSound != null) AudioSource.PlayClipAtPoint(hitSound, transform.position);
        }

        private IEnumerator Flash()
        {
            for (int i = 0; i < flashCount; i++)
            {
                SetFlash(true);
                yield return new WaitForSeconds(flashDuration * 0.5f);
                SetFlash(false);
                yield return new WaitForSeconds(flashDuration * 0.5f);
            }
        }

        /// <summary>Flash on = override _BaseColor; off = clear the override so every material keeps its own color.</summary>
        private void SetFlash(bool on)
        {
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                if (on)
                {
                    block.Clear();
                    block.SetColor("_BaseColor", flashColor);
                    r.SetPropertyBlock(block);
                }
                else r.SetPropertyBlock(null);
            }
        }
    }
}
