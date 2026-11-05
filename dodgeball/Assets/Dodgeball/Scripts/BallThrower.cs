using UnityEngine;

namespace Dodgeball
{
    /// <summary>
    /// Picking up, holding and throwing a ball. Used by both PlayerController and AIController.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class BallThrower : MonoBehaviour
    {
        [SerializeField] private Transform holdPoint;
        [SerializeField] private float pickupRadius = 1.3f;
        [Tooltip("Balls this close to the center line can be picked up from either side.")]
        [SerializeField] private float centerLineTolerance = 0.4f;
        [SerializeField] private bool autoPickup = true;

        [Header("Holding")]
        [Tooltip("How far the held ball sinks into the body outline (1 = just touching, lower = tighter).")]
        [Range(0.3f, 1.2f)] [SerializeField] private float holdTightness = 0.6f;
        [Tooltip("Sideways offset of the held ball (positive = right hand).")]
        [SerializeField] private float holdSideOffset = 0.12f;
        [Tooltip("Height of the held ball relative to the character's center.")]
        [SerializeField] private float holdHeight = 0.1f;

        [Header("Throw")]
        [SerializeField] private float minThrowSpeed = 16f;
        [SerializeField] private float maxThrowSpeed = 28f;
        [Tooltip("Seconds of holding the throw button to reach max speed.")]
        [SerializeField] private float maxChargeTime = 1f;
        [Tooltip("Extra upward speed on top of the calculated arc (0 = arrives exactly at the aim height).")]
        [SerializeField] private float extraLift = 0.3f;
        [Tooltip("Height above the floor the throw is aimed at (about chest height).")]
        [SerializeField] private float aimHeight = 1.1f;
        [Tooltip("Minimum time between picking up and throwing.")]
        [SerializeField] private float throwDelayAfterPickup = 0.15f;

        private CharacterMotor motor;
        private Collider ownCollider;
        private float pickupTime;

        public Ball HeldBall { get; private set; }
        public bool HasBall => HeldBall != null;
        public float MaxChargeTime => maxChargeTime;
        public float MinThrowSpeed => minThrowSpeed;
        public bool AutoPickup { get => autoPickup; set => autoPickup = value; }
        public float PickupRadius => pickupRadius;
        public bool CanThrow => HasBall && Time.time >= pickupTime + throwDelayAfterPickup;

        private void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            ownCollider = GetComponent<Collider>();
            if (holdPoint == null)
            {
                holdPoint = new GameObject("HoldPoint").transform;
                holdPoint.SetParent(transform, false);
                holdPoint.localPosition = new Vector3(0.45f, 0.3f, 0.45f);
            }
        }

        private void Update()
        {
            if (autoPickup && !HasBall) TryPickup();
        }

        /// <summary>True if this ball may be picked up by us right now.</summary>
        public bool CanPickUp(Ball ball)
        {
            if (ball == null || ball.State != Ball.BallState.Dead) return false;
            ArenaBounds arena = ArenaBounds.Instance;
            if (arena == null) return true;
            float signed = arena.SignedDistance(ball.transform.position);
            return Mathf.Abs(signed) <= centerLineTolerance || arena.SideOf(ball.transform.position) == motor.Side;
        }

        /// <summary>Picks up the closest legal ball within reach. Returns true on success.</summary>
        public bool TryPickup()
        {
            if (HasBall) return false;
            Ball best = null;
            float bestDist = pickupRadius * pickupRadius;
            foreach (Ball ball in Ball.All)
            {
                if (!CanPickUp(ball)) continue;
                float d = ArenaBounds.Flat(ball.transform.position - transform.position).sqrMagnitude;
                if (d <= bestDist) { bestDist = d; best = ball; }
            }
            if (best == null) return false;

            HeldBall = best;
            pickupTime = Time.time;
            PlaceHoldPoint(best.Radius);
            best.Attach(this, holdPoint);
            return true;
        }

        /// <summary>Throws toward a world point. charge01: 0 = weak, 1 = full power.</summary>
        public bool ThrowAt(Vector3 targetPoint, float charge01)
        {
            if (!CanThrow) return false;

            Vector3 from = holdPoint.position;
            Vector3 dir = ArenaBounds.Flat(targetPoint - from);
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir.Normalize();

            float speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, Mathf.Clamp01(charge01));

            // Ballistic arc: pick the vertical speed so the ball arrives at chest height at the target
            // instead of diving into the floor (slow throws) or flying over the target (fast throws).
            float floorY = ArenaBounds.Instance != null ? ArenaBounds.Instance.FloorY : 0f;
            float distance = ArenaBounds.Flat(targetPoint - from).magnitude;
            float flightTime = Mathf.Max(distance / speed, 0.05f);
            float g = -Physics.gravity.y;
            float dy = (floorY + aimHeight) - from.y;
            float vy = dy / flightTime + 0.5f * g * flightTime + extraLift;
            vy = Mathf.Clamp(vy, -4f, 8f);
            Vector3 velocity = dir * speed + Vector3.up * vy;

            Ball ball = HeldBall;
            HeldBall = null;
            ball.Launch(velocity, ownCollider);
            return true;
        }

        /// <summary>
        /// Puts the hold point right against the front of the body (slightly to the throwing side),
        /// so the held ball touches the character instead of floating next to it, whatever its size.
        /// </summary>
        private void PlaceHoldPoint(float ballRadius)
        {
            float bodyRadius = 0.5f;
            if (ownCollider is CharacterController cc) bodyRadius = cc.radius;
            float scale = Mathf.Max(transform.lossyScale.x, 0.0001f);
            float forward = (bodyRadius + ballRadius * holdTightness) / scale;
            holdPoint.localPosition = new Vector3(holdSideOffset / scale, holdHeight / scale, forward);
        }

        /// <summary>Speed the ball will have for a given charge (the AI uses this to lead its target).</summary>
        public float SpeedForCharge(float charge01) => Mathf.Lerp(minThrowSpeed, maxThrowSpeed, Mathf.Clamp01(charge01));

        /// <summary>Called by Ball when it is respawned while held.</summary>
        public void ReleaseWithoutThrow() => HeldBall = null;
    }
}
