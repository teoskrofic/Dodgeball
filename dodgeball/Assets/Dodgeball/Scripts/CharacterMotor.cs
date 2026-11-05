using UnityEngine;

namespace Dodgeball
{
    /// <summary>
    /// Moves a character with a CharacterController and keeps it inside its own half.
    /// Both PlayerController and AIController only call Move(direction); DodgeAbility calls StartDash.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class CharacterMotor : MonoBehaviour
    {
        [Tooltip("-1 = player's half, +1 = AI's half.")]
        [SerializeField] private int side = -1;
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float turnSpeed = 720f;
        [SerializeField] private float gravity = -20f;

        private CharacterController controller;
        private Vector3 desiredMove;
        private float verticalVelocity;
        private Vector3 dashVelocity;
        private float dashTimeLeft;

        public int Side => side;
        public bool IsDashing => dashTimeLeft > 0f;
        public float MoveSpeed => moveSpeed;
        /// <summary>Current horizontal velocity (used by the AI to predict the player).</summary>
        public Vector3 Velocity { get; private set; }
        /// <summary>Last non-zero movement direction (used as the default dodge direction).</summary>
        public Vector3 LastMoveDirection { get; private set; } = Vector3.right;
        public bool CanMove { get; set; } = true;
        /// <summary>When set, the character rotates toward this point instead of its move direction.</summary>
        public Vector3? LookTarget { get; set; }

        private void Awake() => controller = GetComponent<CharacterController>();

        /// <summary>Call every frame with a horizontal direction (magnitude 0..1).</summary>
        public void Move(Vector3 direction)
        {
            direction.y = 0f;
            desiredMove = Vector3.ClampMagnitude(direction, 1f);
            if (desiredMove.sqrMagnitude > 0.01f) LastMoveDirection = desiredMove.normalized;
        }

        public void StartDash(Vector3 direction, float speed, float duration)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = LastMoveDirection;
            dashVelocity = direction.normalized * speed;
            dashTimeLeft = duration;
        }

        /// <summary>Instantly places the character (used at game start).</summary>
        public void Teleport(Vector3 position)
        {
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
        }

        private void Update()
        {
            Vector3 horizontal = Vector3.zero;
            if (dashTimeLeft > 0f)
            {
                dashTimeLeft -= Time.deltaTime;
                horizontal = dashVelocity;
            }
            else if (CanMove)
            {
                horizontal = desiredMove * moveSpeed;
            }

            verticalVelocity = controller.isGrounded ? -1f : verticalVelocity + gravity * Time.deltaTime;
            Vector3 before = transform.position;
            controller.Move((horizontal + Vector3.up * verticalVelocity) * Time.deltaTime);

            // Hard clamp to our half (also guards against being pushed across by physics).
            ArenaBounds arena = ArenaBounds.Instance;
            if (arena != null)
            {
                Vector3 clamped = arena.ClampToSide(transform.position, side, controller.radius);
                if ((clamped - transform.position).sqrMagnitude > 0.0001f)
                {
                    controller.enabled = false;
                    transform.position = clamped;
                    controller.enabled = true;
                }
            }

            Velocity = ArenaBounds.Flat(transform.position - before) / Mathf.Max(Time.deltaTime, 0.0001f);
            Rotate(horizontal);
        }

        private void Rotate(Vector3 horizontal)
        {
            Vector3 lookDir = LookTarget.HasValue ? ArenaBounds.Flat(LookTarget.Value - transform.position) : horizontal;
            if (lookDir.sqrMagnitude < 0.01f) return;
            Quaternion target = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        }
    }
}
