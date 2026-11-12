using System.Collections.Generic;
using UnityEngine;

namespace Dodgeball
{
    /// <summary>
    /// One dodgeball. States: Dead (lying around, can be picked up) -> Held -> Live (thrown, deals damage)
    /// -> Dead again as soon as it touches anything that is not a character.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class Ball : MonoBehaviour
    {
        public enum BallState { Dead, Held, Live }

        [SerializeField] private Color liveTint = new Color(1f, 0.35f, 0.25f);
        [SerializeField] private AudioClip hitSound;
        [SerializeField] private AudioClip bounceSound;

        [Header("Physics")]
        [Tooltip("Invisible wall on the court lines: how much speed a ball keeps when it bounces off it.")]
        [Range(0f, 1f)] [SerializeField] private float boundaryBounce = 0.45f;
        [Tooltip("Drag while a dead ball rolls on the floor (higher = stops sooner).")]
        [SerializeField] private float rollingDamping = 1.2f;
        [Tooltip("A rolling dead ball slower than this (m/s) is stopped completely.")]
        [SerializeField] private float stopSpeed = 0.25f;
        [Tooltip("Extra gravity multiplier while a dead ball is in the air (makes bounces feel less floaty).")]
        [SerializeField] private float deadGravityMultiplier = 1.4f;

        /// <summary>All balls in the scene (the AI reads this list).</summary>
        public static readonly List<Ball> All = new List<Ball>();

        private Rigidbody body;
        private SphereCollider sphere;
        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private Collider throwerCollider;

        public BallState State { get; private set; } = BallState.Dead;
        public BallThrower Thrower { get; private set; }
        /// <summary>Side-wall bounces since this ball was thrown (Side Bounce mode).</summary>
        public int Bounces { get; private set; }
        public Rigidbody Body => body;
        public float Radius => sphere.radius * transform.lossyScale.x;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            sphere = GetComponent<SphereCollider>();
            renderers = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxAngularVelocity = 30f;
            body.sleepThreshold = 0.05f;
        }

        private void FixedUpdate()
        {
            if (State == BallState.Held) return;
            ArenaBounds arena = ArenaBounds.Instance;
            if (arena == null) return;

            bool grounded = transform.position.y <= arena.FloorY + Radius + 0.05f;

            // Live balls fly freely; dead balls roll to a stop instead of sliding forever.
            body.linearDamping = State == BallState.Live ? 0f : (grounded ? rollingDamping : 0.05f);
            body.angularDamping = grounded ? 1f : 0.05f;
            if (State == BallState.Dead && !grounded)
                body.AddForce(Physics.gravity * (deadGravityMultiplier - 1f), ForceMode.Acceleration);
            // Side Bounce mode: thrown balls fly flatter so they can travel wall to wall.
            if (State == BallState.Live && GameManager.Instance != null)
            {
                float scale = GameManager.Instance.LiveBallGravityScale;
                if (scale < 1f) body.AddForce(Physics.gravity * (scale - 1f), ForceMode.Acceleration);
            }

            KeepInsideCourt(arena);

            if (State == BallState.Dead && grounded && body.linearVelocity.sqrMagnitude < stopSpeed * stopSpeed)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Invisible walls exactly on the court lines (the same area the characters can reach),
        /// so a ball can never end up somewhere nobody can pick it up.
        /// In Side Bounce mode a thrown ball bounces off the two side walls and stays live;
        /// the back walls (end lines) always kill it.
        /// </summary>
        private void KeepInsideCourt(ArenaBounds arena)
        {
            float r = Radius;
            Vector3 pos = body.position;
            Vector3 vel = body.linearVelocity;
            Vector3 local = pos - arena.Center;

            GameManager.SideBounceRules rules = State == BallState.Live && GameManager.Instance != null
                ? GameManager.Instance.ActiveBounceRules : null;
            bool canSideBounce = rules != null && Bounces < rules.maxBounces;

            bool hitEnd = ClampAxis(ref pos, ref vel, local, arena.LengthAxis, arena.HalfLength - r, boundaryBounce);
            bool hitSide = ClampAxis(ref pos, ref vel, local, arena.WidthAxis, arena.HalfWidth - r, canSideBounce ? 1f : boundaryBounce);
            if (!hitEnd && !hitSide) return;

            if (State == BallState.Live)
            {
                if (hitEnd || !canSideBounce)
                {
                    PlaySound(bounceSound, 0.6f);
                    MakeDead();
                }
                else
                {
                    // Clean bank shot: mirror the horizontal direction, lose a bit of speed, stay dangerous.
                    Vector3 flat = ArenaBounds.Flat(vel) * rules.speedKept;
                    vel = flat + Vector3.up * vel.y;
                    Bounces++;
                    PlaySound(bounceSound, 0.8f);
                }
            }

            body.position = pos;
            body.linearVelocity = vel;
        }

        private static bool ClampAxis(ref Vector3 pos, ref Vector3 vel, Vector3 local, Vector3 axis, float limit, float bounciness)
        {
            float d = Vector3.Dot(local, axis);
            if (Mathf.Abs(d) <= limit) return false;

            float sign = Mathf.Sign(d);
            pos += axis * (sign * limit - d);                     // push back onto the line
            float outward = Vector3.Dot(vel, axis) * sign;
            if (outward > 0f) vel -= axis * (sign * outward * (1f + bounciness)); // reflect (1 = no energy lost)
            return true;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Update()
        {
            // Respawn if the ball flew out of the arena.
            if (State != BallState.Held && ArenaBounds.Instance != null && ArenaBounds.Instance.IsOutOfArena(transform.position))
                Respawn(ArenaBounds.Instance.CenterLinePoint(Random.Range(-0.5f, 0.5f)));
        }

        /// <summary>Called by BallThrower when it grabs the ball.</summary>
        public void Attach(BallThrower newHolder, Transform holdPoint)
        {
            SetLiveCollisionIgnore(false);
            Thrower = newHolder;
            State = BallState.Held;
            // Interpolation on a parented kinematic body makes Unity keep drawing it at its old
            // physics position, so the ball lags/drifts away from a moving character. Turn it off while held.
            body.interpolation = RigidbodyInterpolation.None;
            body.isKinematic = true;
            sphere.enabled = false;
            transform.SetParent(holdPoint, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        private void LateUpdate()
        {
            // Glue the held ball to the hold point every frame, after all movement for this frame.
            if (State == BallState.Held && transform.parent != null)
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Called by BallThrower when it throws the ball.</summary>
        public void Launch(Vector3 velocity, Collider ownCollider)
        {
            transform.SetParent(null, true);
            // Start the physics body exactly where the ball is drawn (at the hand), then fly smoothly.
            body.position = transform.position;
            body.rotation = transform.rotation;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            sphere.enabled = true;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 10f;

            throwerCollider = ownCollider;
            SetLiveCollisionIgnore(true); // a ball can never hit its own thrower
            State = BallState.Live;
            Bounces = 0;
            SetTint(true);
        }

        public void Respawn(Vector3 position)
        {
            if (State == BallState.Held && Thrower != null) Thrower.ReleaseWithoutThrow();
            transform.SetParent(null, true);
            body.isKinematic = false;
            sphere.enabled = true;
            transform.position = position + Vector3.up * (Radius + 0.5f);
            body.position = transform.position;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            MakeDead();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (State != BallState.Live) { PlaySound(bounceSound, 0.3f); return; }

            Health target = collision.collider.GetComponentInParent<Health>();
            if (target != null && Thrower != null && target.gameObject != Thrower.gameObject)
            {
                if (target.TakeHit()) PlaySound(hitSound, 1f);
            }
            else
            {
                PlaySound(bounceSound, 0.6f);
            }
            // Anything touched (character, floor, wall) turns the ball into a dead ball.
            MakeDead();
        }

        private void MakeDead()
        {
            State = BallState.Dead;
            Bounces = 0;
            SetLiveCollisionIgnore(false);
            Thrower = null;
            SetTint(false);
        }

        private void SetLiveCollisionIgnore(bool ignore)
        {
            if (throwerCollider != null) Physics.IgnoreCollision(sphere, throwerCollider, ignore);
            if (!ignore) throwerCollider = null;
        }

        private void SetTint(bool live)
        {
            // Can be called before Awake (e.g. Respawn right after Instantiate on an inactive object).
            if (block == null) block = new MaterialPropertyBlock();
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
            {
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", live ? liveTint : Color.white);
                r.SetPropertyBlock(block);
            }
        }

        private void PlaySound(AudioClip clip, float volume)
        {
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, volume);
        }
    }
}
