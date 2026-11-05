using UnityEngine;
using UnityEngine.InputSystem;

namespace Dodgeball
{
    /// <summary>
    /// Reads player input (new Input System) and forwards it to CharacterMotor, DodgeAbility and BallThrower.
    /// Default bindings are created in code, so no Input Actions asset is required.
    /// You can still edit/add bindings on the component in the Inspector.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(DodgeAbility), typeof(BallThrower))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private Camera aimCamera;
        [Tooltip("Hold the throw button to charge a stronger throw.")]
        [SerializeField] private bool chargeThrow = true;

        [Header("Input (filled with defaults if empty)")]
        [SerializeField] private InputAction moveAction = new InputAction("Move", InputActionType.Value);
        [SerializeField] private InputAction aimStickAction = new InputAction("AimStick", InputActionType.Value);
        [SerializeField] private InputAction dodgeAction = new InputAction("Dodge", InputActionType.Button);
        [SerializeField] private InputAction pickupAction = new InputAction("Pickup", InputActionType.Button);
        [SerializeField] private InputAction throwAction = new InputAction("Throw", InputActionType.Button);

        private CharacterMotor motor;
        private DodgeAbility dodge;
        private BallThrower thrower;
        private float chargeStart = -1f;
        private Vector3 aimPoint;

        /// <summary>0..1 charge of the current throw (for UI).</summary>
        public float Charge01 => chargeStart < 0f ? 0f : Mathf.Clamp01((Time.time - chargeStart) / thrower.MaxChargeTime);

        private void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            dodge = GetComponent<DodgeAbility>();
            thrower = GetComponent<BallThrower>();
            if (aimCamera == null) aimCamera = Camera.main;
            AddDefaultBindings();
        }

        private void AddDefaultBindings()
        {
            if (moveAction.bindings.Count == 0)
            {
                moveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
                moveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                    .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
                moveAction.AddBinding("<Gamepad>/leftStick");
            }
            if (aimStickAction.bindings.Count == 0) aimStickAction.AddBinding("<Gamepad>/rightStick");
            if (dodgeAction.bindings.Count == 0)
            {
                dodgeAction.AddBinding("<Keyboard>/space");
                dodgeAction.AddBinding("<Gamepad>/buttonSouth");
            }
            if (pickupAction.bindings.Count == 0)
            {
                pickupAction.AddBinding("<Keyboard>/e");
                pickupAction.AddBinding("<Gamepad>/buttonWest");
            }
            if (throwAction.bindings.Count == 0)
            {
                throwAction.AddBinding("<Mouse>/leftButton");
                throwAction.AddBinding("<Gamepad>/rightTrigger");
            }
        }

        private void OnEnable()
        {
            moveAction.Enable(); aimStickAction.Enable(); dodgeAction.Enable(); pickupAction.Enable(); throwAction.Enable();
        }

        private void OnDisable()
        {
            moveAction.Disable(); aimStickAction.Disable(); dodgeAction.Disable(); pickupAction.Disable(); throwAction.Disable();
            motor.Move(Vector3.zero);
            motor.LookTarget = null;
        }

        private void Update()
        {
            Vector3 moveDir = CameraRelative(moveAction.ReadValue<Vector2>());
            motor.Move(moveDir);

            UpdateAim();
            motor.LookTarget = thrower.HasBall ? aimPoint : (Vector3?)null;

            if (dodgeAction.WasPressedThisFrame()) dodge.TryDodge(moveDir);
            if (pickupAction.WasPressedThisFrame()) thrower.TryPickup();

            HandleThrow();
        }

        private void HandleThrow()
        {
            if (!thrower.HasBall) { chargeStart = -1f; return; }

            if (!chargeThrow)
            {
                if (throwAction.WasPressedThisFrame()) thrower.ThrowAt(aimPoint, 0.5f);
                return;
            }

            if (throwAction.WasPressedThisFrame()) chargeStart = Time.time;
            if (throwAction.WasReleasedThisFrame() && chargeStart >= 0f)
            {
                if (thrower.ThrowAt(aimPoint, Charge01)) chargeStart = -1f;
            }
        }

        /// <summary>Aim = mouse position on the floor plane, or right stick direction on a gamepad.</summary>
        private void UpdateAim()
        {
            Vector2 stick = aimStickAction.ReadValue<Vector2>();
            if (stick.sqrMagnitude > 0.2f)
            {
                aimPoint = transform.position + CameraRelative(stick).normalized * 10f;
                return;
            }

            if (Mouse.current == null || aimCamera == null) return;
            float floorY = ArenaBounds.Instance != null ? ArenaBounds.Instance.FloorY : 0f;
            Plane ground = new Plane(Vector3.up, new Vector3(0f, floorY + 1f, 0f));
            Ray ray = aimCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (ground.Raycast(ray, out float enter)) aimPoint = ray.GetPoint(enter);
        }

        /// <summary>Converts 2D input into a world direction relative to the camera (W = away from camera).</summary>
        private Vector3 CameraRelative(Vector2 input)
        {
            if (aimCamera == null) return new Vector3(input.x, 0f, input.y);
            Vector3 forward = ArenaBounds.Flat(aimCamera.transform.forward).normalized;
            Vector3 right = ArenaBounds.Flat(aimCamera.transform.right).normalized;
            return forward * input.y + right * input.x;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(aimPoint, 0.3f);
        }
    }
}
