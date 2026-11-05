using UnityEngine;

namespace Dodgeball
{
    /// <summary>
    /// Drives an optional character model (child of the capsule) from CharacterMotor:
    /// sets common Animator parameters (Speed, MotionSpeed, Grounded) if the model's controller has them.
    /// Works with any model; without an Animator it does nothing.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class CharacterVisual : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [Tooltip("Animator float that receives the horizontal speed in m/s.")]
        [SerializeField] private string speedParameter = "Speed";
        [SerializeField] private float speedDamping = 0.1f;

        private CharacterMotor motor;
        private bool hasSpeed, hasMotionSpeed, hasGrounded;
        private int speedHash, motionSpeedHash, groundedHash;

        private void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return;

            animator.applyRootMotion = false; // movement comes from CharacterMotor
            speedHash = Animator.StringToHash(speedParameter);
            motionSpeedHash = Animator.StringToHash("MotionSpeed");
            groundedHash = Animator.StringToHash("Grounded");
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.nameHash == speedHash && p.type == AnimatorControllerParameterType.Float) hasSpeed = true;
                if (p.nameHash == motionSpeedHash && p.type == AnimatorControllerParameterType.Float) hasMotionSpeed = true;
                if (p.nameHash == groundedHash && p.type == AnimatorControllerParameterType.Bool) hasGrounded = true;
            }
        }

        private void Update()
        {
            if (animator == null) return;
            if (hasSpeed) animator.SetFloat(speedHash, motor.Velocity.magnitude, speedDamping, Time.deltaTime);
            if (hasMotionSpeed) animator.SetFloat(motionSpeedHash, 1f);
            if (hasGrounded) animator.SetBool(groundedHash, true);
        }
    }
}
