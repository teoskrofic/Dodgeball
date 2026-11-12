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
        [Tooltip("0..1 fraction of the full move speed the AI uses.")]
        [Range(0.2f, 1f)] [SerializeField] private float moveSpeed01 = 0.8f;
        [Tooltip("Scales the AI's throw speed (below 1 = slower, easier to dodge).")]
        [Range(0.5f, 1.2f)] [SerializeField] private float throwSpeedMultiplier = 1f;

        [Tooltip("Side Bounce mode: chance (0..1) to bank the throw off a side wall. Set by the GameManager.")]
        [Range(0f, 1f)] [SerializeField] private float bankShotChance = 0f;

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
        // Value = the ball's bounce count when judged; a side-wall bounce changes its path, so it is judged again.
        private readonly Dictionary<Ball, int> judgedBalls = new Dictionary<Ball, int>();
        private readonly List<Ball> forgetBuffer = new List<Ball>();

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

        public void SetBankShotChance(float chance) => bankShotChance = Mathf.Clamp01(chance);

        private void ApplyPreset()
        {
            // aimError = max sideways miss in meters. A standing target (~0.8 m wide hit zone) is hit
            // roughly 0.8 / aimError of the time, so aimError is the main knob, then throw speed.
            switch (difficulty)
            {
                case Difficulty.Easy:   // slow, sloppy, rarely dodges - easy to beat
                    reactionTime = 0.6f; dodgeChance = 0.15f; aimError = 3.2f; prediction = 0f;
                    throwDelayRange = new Vector2(2.2f, 3.8f); throwPower = 0f; throwSpeedMultiplier = 0.7f; moveSpeed01 = 0.55f; break;
                case Difficulty.Normal: // a challenge, but beatable if you keep moving and dodge
                    reactionTime = 0.35f; dodgeChance = 0.45f; aimError = 1.6f; prediction = 0.4f;
                    throwDelayRange = new Vector2(1.2f, 2.2f); throwPower = 0.3f; throwSpeedMultiplier = 0.9f; moveSpeed01 = 0.8f; break;
                case Difficulty.Hard:   // fast, accurate, leads your movement and dodges a lot
                    reactionTime = 0.12f; dodgeChance = 0.8f; aimError = 0.6f; prediction = 0.9f;
                    throwDelayRange = new Vector2(0.4f, 0.9f); throwPower = 0.85f; throwSpeedMultiplier = 1.1f; moveSpeed01 = 1f; break;
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
                thrower.ThrowAt(ComputeAimPoint(), throwPower, throwSpeedMultiplier);
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
                float speed = thrower.SpeedForCharge(throwPower) * throwSpeedMultiplier;
                float flightTime = Vector3.Distance(transform.position, target.position) / Mathf.Max(speed, 0.1f);
                aim += targetMotor.Velocity * flightTime * prediction;
            }
            // Only sideways error makes a ball miss (an error along the throw line still flies through
            // the target), so the error is applied perpendicular to the throw direction.
            Vector3 dir = ArenaBounds.Flat(aim - transform.position).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            aim += side * Random.Range(-aimError, aimError);

            // Side Bounce mode: sometimes bank it off the side wall nearest the target.
            ArenaBounds arena = ArenaBounds.Instance;
            if (arena != null && bankShotChance > 0f && Random.value < bankShotChance)
            {
                int wall = Vector3.Dot(target.position - arena.Center, arena.WidthAxis) >= 0f ? 1 : -1;
                float inset = thrower.HeldBall != null ? thrower.HeldBall.Radius : 0.3f;
                aim = arena.MirrorAcrossSideWall(aim, wall, inset);
            }
            return aim;
        }

        // ---------- Dodging ----------

        private void HandleDodging()
        {
            // 1) Notice new dangerous balls and roll once whether we will react to them.
            foreach (Ball ball in Ball.All)
            {
                if (ball.State != Ball.BallState.Live || ball.Thrower == thrower) continue;
                if (judgedBalls.TryGetValue(ball, out int judgedAtBounce) && judgedAtBounce == ball.Bounces) continue;
                if (!IsThreat(ball, out _)) continue;

                judgedBalls[ball] = ball.Bounces;
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
            forgetBuffer.Clear();
            foreach (Ball b in judgedBalls.Keys)
                if (b == null || b.State != Ball.BallState.Live) forgetBuffer.Add(b);
            foreach (Ball b in forgetBuffer) judgedBalls.Remove(b);
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
            motor.Move(delta.magnitude < 0.3f ? Vector3.zero : delta.normalized * (speed01 * moveSpeed01));
        }

        private bool ReachedMoveTarget() => ArenaBounds.Flat(moveTarget - transform.position).magnitude < 0.5f;

        private void OnDisable()
        {
            if (motor != null) { motor.Move(Vector3.zero); motor.LookTarget = null; }
        }
    }
}
