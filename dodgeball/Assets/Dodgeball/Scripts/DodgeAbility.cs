using UnityEngine;

namespace Dodgeball
{
    /// <summary>
    /// Quick dash with a short invulnerability window and a cooldown.
    /// Shared by the player and the AI so both play by exactly the same rules.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(Health))]
    public class DodgeAbility : MonoBehaviour
    {
        [SerializeField] private float dashSpeed = 16f;
        [SerializeField] private float dashDuration = 0.18f;
        [SerializeField] private float invulnerabilityTime = 0.3f;
        [SerializeField] private float cooldown = 3f;

        private CharacterMotor motor;
        private Health health;
        private float readyTime;

        public bool IsReady => Time.time >= readyTime;
        public float Cooldown => cooldown;
        /// <summary>0 = just used, 1 = ready. Used by the UI fill indicator.</summary>
        public float CooldownProgress => cooldown <= 0f ? 1f : Mathf.Clamp01(1f - (readyTime - Time.time) / cooldown);

        private void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            health = GetComponent<Health>();
        }

        /// <summary>Dashes in 'direction' (falls back to last move direction). Returns false while on cooldown.</summary>
        public bool TryDodge(Vector3 direction)
        {
            if (!IsReady || health.IsDead) return false;

            motor.StartDash(direction, dashSpeed, dashDuration);
            health.SetInvulnerable(invulnerabilityTime);
            readyTime = Time.time + cooldown;
            return true;
        }
    }
}
