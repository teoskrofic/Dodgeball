using System.Collections.Generic;
using UnityEngine;

namespace Dodgeball
{
    /// <summary>
    /// Simple state machine AI: SeekBall -> AimAndThrow, Reposition when idle, and Dodge interrupts any state.
    /// Uses the same CharacterMotor, DodgeAbility and BallThrower as the player.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(DodgeAbility), typeof(BallThrower))]
    public class AIController : MonoBehaviour
    {
        public enum Difficulty { Easy, Normal, Hard, Custom }
        private enum State { Reposition, SeekBall, AimAndThrow }

        [SerializeField] private Transform target;
        [Tooltip("Choosing a preset overwrites the values below. Pick Custom to tune them by hand.")]
        [SerializeField] private Difficulty difficulty = Difficulty.Normal;

        [Header("Difficulty values")]
        [Tooltip("Seconds before the AI reacts to an incoming ball.")]
        [SerializeField] private float reactionTime = 0.25f;
        [Tooltip("Chance (0..1) that the AI tries to dodge a dangerous ball.")]
        [Range(0f, 1f)] [SerializeField] private float dodgeChance = 0.6f;
        [Tooltip("Maximum aiming error in meters at the target.")]
        [SerializeField] private float aimError = 1.2f;
        [Tooltip("0 = aim at current position, 1 = fully lead the target's movement.")]
        [Range(0f, 1f)] [SerializeField] private float prediction = 0.5f;
        [SerializeField] private Vector2 throwDelayRange = new Vector2(0.6f, 1.4f);
        [Tooltip("0..1 charge used for throws.")]
        [Range(0f, 1f)] [SerializeField] private float throwPower = 0.6f;

        [Header("Behaviour")]
        [SerializeField] private float dangerRadius = 1.2f;
        [SerializeField] private float lookAheadTime = 0.8f;
        [SerializeField] private Vector2 repositionInterval = new Vector2(1.5f, 3f);

        private CharacterMotor motor;
        private DodgeAbility dodge;
        private BallThrower thrower;
        private CharacterMotor targetMotor;

        private State state = State.Reposition;
        private Vector3 moveTarget;
        private float nextRepositionTime;
        private float throwTime;
        private Ball seekTarget;
        private Vector3 sidestepDirection;
        private float sidestepUntil;
        // Balls we already decided about, so each ball only gets one dodge roll.
        private readonly Dictionary<Ball, float> pendingDodges = new Dictionary<Ball, float>();
        private readonly HashSet<Ball> judgedBalls = new HashSet<Ball>();

        private void OnValidate() => ApplyPreset();

        private void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            dodge = GetComponent<DodgeAbility>();
            thrower = GetComponent<BallThrower>();
            ApplyPreset();
        }

        private void Start()
        {
            if (target != null) targetMotor = target.GetComponent<CharacterMotor>();
            moveTarget = transform.position;
        }

        public void SetDifficulty(Difficulty newDifficulty)
        {
            difficulty = newDifficulty;
            ApplyPreset();
        }

        private void ApplyPreset()
        {
            switch (difficulty)
            {
                case Difficulty.Easy:
                    reactionTime = 0.45f; dodgeChance = 0.3f; aimError = 2.2f; prediction = 0f;
                    throwDelayRange = new Vector2(1.2f, 2.2f); throwPower = 0.35f; break;
                case Difficulty.Normal:
                    reactionTime = 0.25f; dodgeChance = 0.6f; aimError = 1.2f; prediction = 0.5f;
                    throwDelayRange = new Vector2(0.6f, 1.4f); throwPower = 0.6f; break;
                case Difficulty.Hard:
                    reactionTime = 0.12f; dodgeChance = 0.85f; aimError = 0.5f; prediction = 0.9f;
                    throwDelayRange = new Vector2(0.3f, 0.8f); throwPower = 0.9f; break;
            }
        }

        private void Update()
        {
            HandleDodging();

            switch (state)
            {
                case State.Reposition: UpdateReposition(); break;
                case State.SeekBall: UpdateSeekBall(); break;
                case State.AimAndThrow: UpdateAimAndThrow(); break;
            }

            // Side-step overrides normal movement when the dodge was on cooldown.
            if (Time.time < sidestepUntil) motor.Move(sidestepDirection);
        }

        // ---------- States ----------

        private void UpdateReposition()
        {
            motor.LookTarget = target != null ? target.position : (Vector3?)null;
            if (thrower.HasBall) { EnterAim(); return; }

            seekTarget = FindClosestPickableBall();
            if (seekTarget != null) { state = State.SeekBall; return; }

            if (Time.time >= nextRepositionTime || ReachedMoveTarget())
            {
                moveTarget = ArenaBounds.Instance != null ? ArenaBounds.Instance.RandomPointOnSide(motor.Side, 1f) : transform.position;
                nextRepositionTime = Time.time + Random.Range(repositionInterval.x, repositionInterval.y);
            }
            MoveTowards(moveTarget, 0.6f);
        }

        private void UpdateSeekBall()
        {
            motor.LookTarget = null;
            if (thrower.HasBall) { EnterAim(); return; }
            if (seekTarget == null || !thrower.CanPickUp(seekTarget))
            {
                seekTarget = FindClosestPickableBall();
                if (seekTarget == null) { state = State.Reposition; return; }
            }

            MoveTowards(seekTarget.transform.position, 1f);
            thrower.TryPickup();
        }

        private void UpdateAimAndThrow()
        {
            if (!thrower.HasBall) { state = State.Reposition; return; }
            if (target == null) return;

            motor.LookTarget = target.position;
            // Strafe a little while aiming so the AI isn't a sitting duck.
            MoveTowards(moveTarget, 0.4f);

            if (Time.time >= throwTime && thrower.CanThrow)
            {
                thrower.ThrowAt(ComputeAimPoint(), throwPower);
                state = State.Reposition;
                nextRepositionTime = 0f;
            }
        }

        private void EnterAim()
        {
            state = State.AimAndThrow;
            throwTime = Time.time + Random.Range(throwDelayRange.x, throwDelayRange.y);
            moveTarget = ArenaBounds.Instance != null ? ArenaBounds.Instance.RandomPointOnSide(motor.Side, 1f) : transform.position;
        }

        // ---------- Aiming ----------

        private Vector3 ComputeAimPoint()
        {
            Vector3 aim = target.position;
            if (targetMotor != null && prediction > 0f)
            {
                float speed = thrower.SpeedForCharge(throwPower);
                float flightTime = Vector3.Distance(transform.position, target.position) / Mathf.Max(speed, 0.1f);
                aim += targetMotor.Velocity * flightTime * prediction;
            }
            Vector2 error = Random.insideUnitCircle * aimError;
            return aim + new Vector3(error.x, 0f, error.y);
        }

        // ---------- Dodging ----------

        private void HandleDodging()
        {
            // 1) Notice new dangerous balls and roll once whether we will react to them.
            foreach (Ball ball in Ball.All)
            {
                if (ball.State != Ball.BallState.Live || ball.Thrower == thrower || judgedBalls.Contains(ball)) continue;
                if (!IsThreat(ball, out _)) continue;

                judgedBalls.Add(ball);
                if (Random.value <= dodgeChance) pendingDodges[ball] = Time.time + reactionTime;
            }

            // 2) After the reaction delay, dodge if the ball is still a threat.
            Ball done = null;
            foreach (KeyValuePair<Ball, float> pair in pendingDodges)
            {
                if (Time.time < pair.Value) continue;
                done = pair.Key;
                if (pair.Key != null && pair.Key.State == Ball.BallState.Live && IsThreat(pair.Key, out Vector3 away))
                {
                    if (!dodge.TryDodge(away))
                    {
                        // On cooldown: at least run sideways for a moment.
                        sidestepDirection = away;
                        sidestepUntil = Time.time + 0.4f;
                    }
                }
                break;
            }
            if (done != null) pendingDodges.Remove(done);

            // Forget balls that are no longer live so they can be judged again on the next throw.
            judgedBalls.RemoveWhere(b => b == null || b.State != Ball.BallState.Live);
        }

        /// <summary>Predicts whether a ball will pass within dangerRadius soon. 'away' = best dodge direction.</summary>
        private bool IsThreat(Ball ball, out Vector3 away)
        {
            away = Vector3.zero;
            Vector3 v = ArenaBounds.Flat(ball.Body.linearVelocity);
            Vector3 toMe = ArenaBounds.Flat(transform.position - ball.transform.position);
            float speedSq = v.sqrMagnitude;
            if (speedSq < 1f || Vector3.Dot(v, toMe) <= 0f) return false;

            float t = Vector3.Dot(toMe, v) / speedSq;          // time of closest approach
            if (t > lookAheadTime) return false;

            Vector3 offset = toMe - v * t;                      // my position relative to the closest point
            if (offset.magnitude > dangerRadius) return false;

            Vector3 perpendicular = Vector3.Cross(Vector3.up, v).normalized;
            away = Vector3.Dot(offset, perpendicular) >= 0f ? perpendicular : -perpendicular;
            // Prefer the direction that keeps us inside our half.
            if (ArenaBounds.Instance != null)
            {
                Vector3 test = transform.position + away * 2f;
                if ((ArenaBounds.Instance.ClampToSide(test, motor.Side, 0.5f) - test).sqrMagnitude > 0.5f) away = -away;
            }
            return true;
        }

        // ---------- Helpers ----------

        private Ball FindClosestPickableBall()
        {
            Ball best = null;
            float bestDist = float.MaxValue;
            foreach (Ball ball in Ball.All)
            {
                if (!thrower.CanPickUp(ball)) continue;
                float d = (ball.transform.position - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = ball; }
            }
            return best;
        }

        private void MoveTowards(Vector3 point, float speed01)
        {
            Vector3 delta = ArenaBounds.Flat(point - transform.position);
            motor.Move(delta.magnitude < 0.3f ? Vector3.zero : delta.normalized * speed01);
        }

        private bool ReachedMoveTarget() => ArenaBounds.Flat(moveTarget - transform.position).magnitude < 0.5f;

        private void OnDisable()
        {
            if (motor != null) { motor.Move(Vector3.zero); motor.LookTarget = null; }
        }
    }
}
