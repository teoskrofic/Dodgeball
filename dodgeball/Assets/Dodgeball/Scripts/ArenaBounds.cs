using UnityEngine;

namespace Dodgeball
{
    /// <summary>
    /// Describes the playable court: its center, the long axis (perpendicular to the center line)
    /// and its half-sizes. Side -1 = player's half, side +1 = AI's half.
    /// All other scripts ask this component "which side is this point on?" and
    /// "clamp this point to my half" so the court can have any position/rotation.
    /// </summary>
    public class ArenaBounds : MonoBehaviour
    {
        [Tooltip("Half of the court length along the long axis (center line to baseline).")]
        [SerializeField] private float halfLength = 14f;
        [Tooltip("Half of the court width (sideline to sideline).")]
        [SerializeField] private float halfWidth = 7.5f;
        [Tooltip("Characters keep at least this distance from the center line and sidelines.")]
        [SerializeField] private float edgeMargin = 0.6f;
        [Tooltip("A ball further than this outside the court (or below the floor) is respawned.")]
        [SerializeField] private float outOfBoundsTolerance = 3f;

        public static ArenaBounds Instance { get; private set; }

        public float HalfLength => halfLength;
        public float HalfWidth => halfWidth;
        public Vector3 Center => transform.position;
        public float FloorY => transform.position.y;
        /// <summary>Unit vector from the player's half toward the AI's half.</summary>
        public Vector3 LengthAxis => Flat(transform.forward).normalized;
        public Vector3 WidthAxis => Flat(transform.right).normalized;

        private void Awake() => Instance = this;

        public void Configure(float newHalfLength, float newHalfWidth)
        {
            halfLength = newHalfLength;
            halfWidth = newHalfWidth;
        }

        /// <summary>Signed distance from the center line (negative = player half).</summary>
        public float SignedDistance(Vector3 worldPos) => Vector3.Dot(worldPos - Center, LengthAxis);

        /// <summary>-1 for the player's half, +1 for the AI's half.</summary>
        public int SideOf(Vector3 worldPos) => SignedDistance(worldPos) < 0f ? -1 : 1;

        /// <summary>Clamps a point so it stays inside the given half (y is kept).</summary>
        public Vector3 ClampToSide(Vector3 worldPos, int side, float radius)
        {
            Vector3 local = worldPos - Center;
            float along = Vector3.Dot(local, LengthAxis);
            float across = Vector3.Dot(local, WidthAxis);

            float inner = edgeMargin + radius;
            float outer = halfLength - radius;
            along = side < 0 ? Mathf.Clamp(along, -outer, -inner) : Mathf.Clamp(along, inner, outer);
            across = Mathf.Clamp(across, -halfWidth + radius, halfWidth - radius);

            Vector3 result = Center + LengthAxis * along + WidthAxis * across;
            result.y = worldPos.y;
            return result;
        }

        /// <summary>Random point on the floor of the given half.</summary>
        public Vector3 RandomPointOnSide(int side, float margin)
        {
            float along = Random.Range(edgeMargin + margin, halfLength - margin) * side;
            float across = Random.Range(-halfWidth + margin, halfWidth - margin);
            return Center + LengthAxis * along + WidthAxis * across;
        }

        /// <summary>Point on the center line; t in [-1, 1] goes from one sideline to the other.</summary>
        public Vector3 CenterLinePoint(float t) => Center + WidthAxis * (t * halfWidth);

        /// <summary>Spawn point in the middle of a half.</summary>
        public Vector3 SpawnPoint(int side) => Center + LengthAxis * (side * halfLength * 0.5f);

        /// <summary>
        /// Mirrors a point across a side wall (wallSign -1 or +1), moved 'inset' inside the court.
        /// Throwing at the mirrored point makes the ball bank off that wall and arrive at the original point.
        /// </summary>
        public Vector3 MirrorAcrossSideWall(Vector3 worldPos, int wallSign, float inset)
        {
            float wall = wallSign * (halfWidth - inset);
            float across = Vector3.Dot(worldPos - Center, WidthAxis);
            return worldPos + WidthAxis * (2f * (wall - across));
        }

        public bool IsOutOfArena(Vector3 worldPos)
        {
            Vector3 local = worldPos - Center;
            float along = Mathf.Abs(Vector3.Dot(local, LengthAxis));
            float across = Mathf.Abs(Vector3.Dot(local, WidthAxis));
            return along > halfLength + outOfBoundsTolerance
                || across > halfWidth + outOfBoundsTolerance
                || worldPos.y < FloorY - 2f;
        }

        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.LookRotation(LengthAxis == Vector3.zero ? Vector3.forward : LengthAxis), Vector3.one);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(halfWidth * 2f, 0.05f, halfLength * 2f));
            Gizmos.color = Color.red;
            Gizmos.DrawLine(new Vector3(-halfWidth, 0.05f, 0f), new Vector3(halfWidth, 0.05f, 0f));
        }
    }
}
