using System;
using UnityEngine;

namespace Dodgeball
{
    /// <summary>
    /// Lives of one character. Raises events that UIManager, GameManager and HitFeedback listen to.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] private int maxLives = 3;
        [Tooltip("Short safety window after being hit so one ball can't hit twice.")]
        [SerializeField] private float invulnerableAfterHit = 0.5f;

        private float invulnerableUntil;

        public int Lives { get; private set; }
        public int MaxLives => maxLives;
        public bool IsDead => Lives <= 0;
        public bool IsInvulnerable => Time.time < invulnerableUntil;

        /// <summary>Called with the remaining lives after every hit.</summary>
        public event Action<int> OnDamaged;
        public event Action<Health> OnDied;

        private void Awake() => Lives = maxLives;

        /// <summary>Makes the character immune for 'duration' seconds (used by DodgeAbility).</summary>
        public void SetInvulnerable(float duration)
        {
            invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + duration);
        }

        /// <summary>Returns true if the hit was counted.</summary>
        public bool TakeHit()
        {
            if (IsDead || IsInvulnerable) return false;

            Lives--;
            SetInvulnerable(invulnerableAfterHit);
            OnDamaged?.Invoke(Lives);
            if (Lives <= 0) OnDied?.Invoke(this);
            return true;
        }
    }
}
