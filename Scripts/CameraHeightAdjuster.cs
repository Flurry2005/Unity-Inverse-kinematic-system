using UnityEngine;

public class CameraHeightAdjuster : MonoBehaviour
{
    [Header("Transforms")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform headBone;
    [SerializeField] private Transform ArmatureParent;
    [SerializeField] private Vector3 playerCameraStartLocalValue;
    [SerializeField] private Vector3 previousPlayerCameraLocalValue;

    [Header("Settings")]
    [SerializeField] private float adjustSpeed;
    [SerializeField] private Vector3 positionOffset;

    [Header("Collision")]
    [Tooltip(
        "What the camera may not end up inside. The player's own colliders are skipped "
            + "automatically, so this can stay as Everything."
    )]
    [SerializeField] private LayerMask collisionMask = ~0;

    [Tooltip(
        "Radius of the probe. It needs to cover the near plane's corners, or the corners clip "
            + "into a surface the centre of the camera has cleared. At a 90 degree field of "
            + "view and a 0.01 near plane the corner is about 0.015 out, so 0.05 is ample."
    )]
    [SerializeField] private float collisionRadius = 0.05f;

    [Tooltip("Gap kept between the camera and whatever it stopped against.")]
    [SerializeField] private float collisionSkin = 0.02f;

    [Tooltip("How quickly the camera eases back out, in units per second, once a surface stops blocking it. It always pulls IN instantly.")]
    [SerializeField] private float collisionReturnSpeed = 4f;

    [Tooltip(
        "The body's own collider. The probe starts at its centre, because that point cannot be "
            + "inside a wall -- it is the thing that stops the player entering one. The head "
            + "bone can be: lean against a rock and the animation pushes the head through it, "
            + "and a probe starting in there reports nothing, since a sphere cast ignores what "
            + "it already overlaps. Found automatically when left empty."
    )]
    [SerializeField] private Collider bodyCollider;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    // How far out the camera is currently permitted to sit. Carried between frames so that
    // easing back out is gradual while pulling in is immediate.
    private float allowedDistance = float.PositiveInfinity;

    private void Start()
    {
        playerCameraStartLocalValue = playerCamera.transform.localPosition;
        previousPlayerCameraLocalValue = playerCameraStartLocalValue;
    }
    public void AdjustHeight()
    {
        // Step 1: Compute the offset relative to the head bone's rotation
        Vector3 rotatedOffset = headBone.rotation * positionOffset;

        // Step 2: Compute the target world position
        Vector3 targetWorldPos = headBone.position + rotatedOffset;

        // Step 2b: How far along the line from the body to the camera it may actually sit.
        //
        // Anchored at the body collider's centre, not the head: that point is never inside
        // scenery, so there is always somewhere safe to retreat to. The direction also comes
        // from it rather than from where the camera currently is -- casting along "current
        // position minus anchor" made the probe depend on the camera, the clamp then moved the
        // camera, which changed the direction and the result, and it oscillated against slopes.
        Vector3 anchor = ProbeAnchor();
        Vector3 toTarget = targetWorldPos - anchor;
        float desiredDistance = toTarget.magnitude;
        if (desiredDistance > 0.0001f)
        {
            Vector3 direction = toTarget / desiredDistance;
            float clearDistance = ProbeClearDistance(anchor, direction, desiredDistance);

            // Pull in at once, ease back out. Snapping outwards the instant a surface clears is
            // the other half of the twitch: the probe flickers between hit and miss along an
            // edge, and without this the camera flickers with it.
            allowedDistance = clearDistance < allowedDistance
                ? clearDistance
                : Mathf.MoveTowards(allowedDistance, clearDistance, collisionReturnSpeed * Time.deltaTime);

            targetWorldPos = anchor + direction * allowedDistance;
        }

        // Step 3: Convert to camera parent local space
        Vector3 targetLocalPos = playerCamera.transform.parent.InverseTransformPoint(targetWorldPos);

        // Step 4: Smoothly move the camera
        Vector3 easedLocalPos = Vector3.Lerp(
            playerCamera.transform.localPosition,
            targetLocalPos,
            Time.deltaTime * adjustSpeed
        );

        // Step 5: the camera renders from where it IS, and the lerp leaves it trailing its
        // target, so hold it inside the same limit. Only the distance is capped here -- the
        // limit itself was decided above, independently of this position, so this cannot feed
        // back into the probe.
        Vector3 easedWorldPos = playerCamera.transform.parent.TransformPoint(easedLocalPos);
        Vector3 fromAnchor = easedWorldPos - anchor;
        float easedDistance = fromAnchor.magnitude;
        if (easedDistance > allowedDistance && easedDistance > 0.0001f)
            easedWorldPos = anchor + fromAnchor * (allowedDistance / easedDistance);

        playerCamera.transform.localPosition =
            playerCamera.transform.parent.InverseTransformPoint(easedWorldPos);
    }

    /// <summary>
    /// Where the probe starts: the centre of the body's collider, which is always outside
    /// scenery. Falls back to the head bone when there is no collider to use.
    /// </summary>
    private Vector3 ProbeAnchor()
    {
        if (bodyCollider == null)
            bodyCollider = playerCamera.transform.root.GetComponentInChildren<CapsuleCollider>();
        return bodyCollider != null ? bodyCollider.bounds.center : headBone.position;
    }

    /// <summary>
    /// How far from <paramref name="anchor"/> along <paramref name="direction"/> the camera can
    /// sit without ending up inside something, up to <paramref name="maxDistance"/>.
    ///
    /// Hits on the player's own body are ignored by hierarchy rather than by layer, so no layer
    /// setup is needed: the character's capsule surrounds the head and would otherwise stop the
    /// camera dead at the anchor.
    /// </summary>
    private float ProbeClearDistance(Vector3 anchor, Vector3 direction, float maxDistance)
    {
        int count = Physics.SphereCastNonAlloc(
            anchor,
            collisionRadius,
            direction,
            hitBuffer,
            maxDistance,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );

        Transform self = playerCamera.transform.root;
        float nearest = maxDistance;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hitBuffer[i].collider;
            if (collider == null || collider.transform.IsChildOf(self))
                continue;
            float clear = hitBuffer[i].distance - collisionSkin;
            if (clear < nearest)
                nearest = clear;
        }

        return Mathf.Max(0f, nearest);
    }
}
