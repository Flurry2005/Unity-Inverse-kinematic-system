using UnityEngine;
using UnityEngine.AI;

public class ArmatureIK : MonoBehaviour
{
    public enum OptionType { NavMeshAgent, Rigidbody}

    [Header("Character Type")]
    public OptionType SelectedOption;
    public bool UsingNavMeshAgent;
    public bool UsingRigidbody;

    //Hidden based of selectedOption
    [SerializeField] private NavMeshAgent navAgent;
    [SerializeField] private Rigidbody characterRigidBody;


    [Header("Transforms")]
    [Tooltip("On a four-legged character: the front right foot.")]
    [SerializeField] private Transform rightFoot;
    [Tooltip("On a four-legged character: the front left foot.")]
    [SerializeField] private Transform leftFoot;
    [Tooltip("Four-legged only: the hind right foot. Leave both hind feet empty on a two-legged character.")]
    [SerializeField] private Transform hindRightFoot;
    [Tooltip("Four-legged only: the hind left foot.")]
    [SerializeField] private Transform hindLeftFoot;
    [SerializeField] private Transform armatureParent;

    [Header("Settings")]
    [SerializeField] private float rayYOffset = 1;
    [SerializeField] private float rayDistance = 0.1f;
    [SerializeField] private float AdjustSpeed;
    public bool UpdateHeight = true;
    [SerializeField] private bool manualUpdateToggle;

    [Header("Four-Legged")]
    [Tooltip("Tilts the armature parent (pitch and roll) to the ground under the four feet.")]
    [SerializeField] private bool alignToGround = true;
    [Tooltip("Largest tilt from upright, in degrees.")]
    [SerializeField] private float maxTiltAngle = 35f;
    [SerializeField] private float tiltSpeed = 6f;

    [Header("Activity")]
    [Tooltip("How fast the feet blend in and out of IK when Active changes, per second.")]
    [SerializeField] private float fadeSpeed = 4f;

    [Header("LayerMasks")]
    [SerializeField] private LayerMask hitLayers;
    [SerializeField] private LayerMask ignoreLayers;

    [Header("Armature Live Values")]
    [SerializeField] private float previousArmatureParentYValue = 0;
    [SerializeField] private float originalArmatureParentYValue;
    [SerializeField] public float currentYoffset;
    [SerializeField] private float previousArmatureParentZRotation = 0;

    private Quaternion restLocalRotation;
    private int lastUpdateFrame = -1;
    private bool lastUpdateResult;
    private Collider[] ownColliders = System.Array.Empty<Collider>();
    private readonly RaycastHit[] rayHits = new RaycastHit[16];

    public bool IsFourLegged => hindRightFoot != null && hindLeftFoot != null;

    /// <summary>
    /// Off while the feet must keep the animation's pose, e.g. a creature lying down: they fade
    /// out of IK, while the armature still settles onto and tilts with the ground.
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>How much the feet are in IK, easing toward 1 while Active and 0 while not.</summary>
    public float Weight { get; private set; } = 1f;

    private void Awake()
    {
        Transform root = navAgent != null ? navAgent.transform
            : characterRigidBody != null ? characterRigidBody.transform
            : transform.root;
        ownColliders = root.GetComponentsInChildren<Collider>(true);
    }

    private void Start()
    {
        previousArmatureParentYValue = armatureParent.transform.position.y;
        restLocalRotation = armatureParent.localRotation;
    }

    /// <summary>
    /// Nearest ground straight down from <paramref name="origin"/>, skipping the character's own
    /// colliders: a mesh collider that follows the body would otherwise catch the foot rays,
    /// which start above the feet.
    /// </summary>
    public bool GroundRay(Vector3 origin, float length, int mask, out RaycastHit ground)
    {
        ground = default;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, length, mask);
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (rayHits[i].distance >= nearest || System.Array.IndexOf(ownColliders, rayHits[i].collider) >= 0)
                continue;
            nearest = rayHits[i].distance;
            ground = rayHits[i];
        }
        return nearest < float.MaxValue;
    }

    /// <summary>
    /// Settles the armature onto the ground. Every foot calls it, but it runs once per frame:
    /// later calls return the first result, so the body is not eased once per foot.
    /// </summary>
    public bool UpdateArmature()
    {
        if (lastUpdateFrame == Time.frameCount)
            return lastUpdateResult;
        lastUpdateFrame = Time.frameCount;
        Weight = Mathf.MoveTowards(Weight, Active ? 1f : 0f, Time.deltaTime * fadeSpeed);
        lastUpdateResult = manualUpdateToggle && (UsingNavMeshAgent || UsingRigidbody) && SettleArmature();
        return lastUpdateResult;
    }

    /// <summary>True for the first call in a frame: once-per-frame work hangs off it.</summary>
    public bool FirstCallThisFrame => lastUpdateFrame != Time.frameCount;

    private bool SettleArmature()
    {
        originalArmatureParentYValue = armatureParent.transform.position.y;

        //Zero offset and recalc later
        currentYoffset = 0;

        // Settles whether Active or not: only the feet leave IK, the body still sits on and
        // tilts with the ground.
        bool fourLegged = IsFourLegged;
        if (!UpdateHeight
            || !TryGround(rightFoot, out Vector3 frontRight)
            || !TryGround(leftFoot, out Vector3 frontLeft))
            return Fail(fourLegged);

        Vector3 hindRight = default, hindLeft = default;
        if (fourLegged && (!TryGround(hindRightFoot, out hindRight) || !TryGround(hindLeftFoot, out hindLeft)))
            return Fail(true);

        float targetY;
        if (fourLegged)
        {
            Vector3 normal = GroundNormal(frontRight, frontLeft, hindRight, hindLeft);
            Vector3 centre = (frontRight + frontLeft + hindRight + hindLeft) * 0.25f;

            // Body height from the ground plane under the armature parent rather than from the
            // lowest foot, or a slope would sink the whole body to the hind feet. Lowered by
            // however far the lowest foot sits under that plane, so every leg can still reach.
            float below = Mathf.Min(
                Mathf.Min(frontRight.y - PlaneY(centre, normal, frontRight), frontLeft.y - PlaneY(centre, normal, frontLeft)),
                Mathf.Min(hindRight.y - PlaneY(centre, normal, hindRight), hindLeft.y - PlaneY(centre, normal, hindLeft)));
            targetY = PlaneY(centre, normal, armatureParent.position) + Mathf.Min(0f, below);

            Tilt(alignToGround ? normal : Vector3.up);
        }
        else
            targetY = Mathf.Min(frontRight.y, frontLeft.y);

        Vector3 NewArmatureParentWorldPos = armatureParent.transform.position;
        NewArmatureParentWorldPos.y = Mathf.Lerp(previousArmatureParentYValue, targetY, Time.deltaTime * AdjustSpeed);
        previousArmatureParentYValue = NewArmatureParentWorldPos.y;
        armatureParent.transform.position = NewArmatureParentWorldPos;

        //Calculate correct Y offset
        currentYoffset = targetY - originalArmatureParentYValue;
        return true;
    }

    private bool TryGround(Transform foot, out Vector3 point)
    {
        point = default;
        if (!GroundRay(foot.position + new Vector3(0, rayYOffset, 0), rayDistance, hitLayers & ~ignoreLayers.value, out var hit))
            return false;
        //Ensure armature is not offset on a Roof
        if (hit.normal.y < 0)
            return false;
        point = hit.point;
        return true;
    }

    private bool Fail(bool fourLegged)
    {
        previousArmatureParentYValue = originalArmatureParentYValue;
        if (fourLegged)
            Tilt(Vector3.up);
        return false;
    }

    // Up of the plane through the four feet: front-to-hind crossed with left-to-right.
    private Vector3 GroundNormal(Vector3 frontRight, Vector3 frontLeft, Vector3 hindRight, Vector3 hindLeft)
    {
        Vector3 forward = (frontRight + frontLeft) - (hindRight + hindLeft);
        Vector3 right = (frontRight + hindRight) - (frontLeft + hindLeft);
        Vector3 normal = Vector3.Cross(forward, right);
        if (normal.sqrMagnitude < 1e-8f)
            return Vector3.up;
        normal.Normalize();
        if (normal.y < 0f)
            normal = -normal;
        return Vector3.RotateTowards(Vector3.up, normal, maxTiltAngle * Mathf.Deg2Rad, 0f);
    }

    private static float PlaneY(Vector3 pointOnPlane, Vector3 normal, Vector3 at) =>
        pointOnPlane.y - (normal.x * (at.x - pointOnPlane.x) + normal.z * (at.z - pointOnPlane.z)) / normal.y;

    // Pitch and roll only: the rest rotation turned so its up meets the ground's, never yawed.
    private void Tilt(Vector3 worldUp)
    {
        Transform parent = armatureParent.parent;
        Vector3 localUp = parent != null ? parent.InverseTransformDirection(worldUp) : worldUp;
        Quaternion target = Quaternion.FromToRotation(Vector3.up, localUp) * restLocalRotation;
        armatureParent.localRotation = Quaternion.Slerp(armatureParent.localRotation, target, Time.deltaTime * tiltSpeed);
    }

    public void LerpArmatureRotation(float target)
    {
        float targetZ = target;

        Vector3 currentEuler = armatureParent.localEulerAngles;

        if (currentEuler.z > 180f) currentEuler.z -= 360f;

        float newZ = Mathf.Lerp(previousArmatureParentZRotation, targetZ, Time.deltaTime * 5f);

        previousArmatureParentZRotation = newZ;

        armatureParent.localRotation = Quaternion.Euler(currentEuler.x, currentEuler.y, newZ);
    }
}
