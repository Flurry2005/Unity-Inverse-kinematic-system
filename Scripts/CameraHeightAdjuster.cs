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

    [Header("Wall Clipping")]
    [Tooltip(
        "The body's own collider. When the head reaches into a wall, the visible body (Armature "
            + "Parent) moves back until the camera clears it, found by a short sideways probe from "
            + "this capsule's centre line, at the camera's height, to the head. Found "
            + "automatically when left empty."
    )]
    [SerializeField] private CapsuleCollider bodyCollider;

    [Tooltip("What the camera may not end up inside. The player's own colliders are skipped automatically.")]
    [SerializeField] private LayerMask clipMask = ~0;

    [Tooltip(
        "Radius of the probe. It must cover the near plane's corners: at a 90 degree field of "
            + "view and a 0.01 near plane they are about 0.02 out."
    )]
    [SerializeField] private float clipRadius = 0.05f;

    [Tooltip("Gap kept between the camera and the wall it stopped at.")]
    [SerializeField] private float clipSkin = 0.02f;

    [Tooltip("How fast the camera follows the head back out once the wall no longer blocks it, in units per second. It always stops at a wall at once.")]
    [SerializeField] private float clipReturnSpeed = 2f;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    // How far from the centre line the camera may sit, carried between frames so a probe
    // flickering along an edge eases back out instead of snapping.
    private float allowedDistance = float.PositiveInfinity;

    // ArmatureParent's own local x/z. The body shift is applied on top of it every frame, from
    // scratch, so it can never accumulate.
    private Vector2 armatureRestLocalXZ;

    private void Start()
    {
        playerCameraStartLocalValue = playerCamera.transform.localPosition;
        previousPlayerCameraLocalValue = playerCameraStartLocalValue;
        if (ArmatureParent != null)
            armatureRestLocalXZ = new Vector2(ArmatureParent.localPosition.x, ArmatureParent.localPosition.z);
    }
    public void AdjustHeight()
    {
        // Undo last frame's body shift first, so the head below is where the animation puts it.
        // Measuring from the shifted head would make the shift feed back into itself.
        SetBodyShift(Vector3.zero);

        // Step 1: Compute the offset relative to the head bone's rotation
        Vector3 rotatedOffset = headBone.rotation * positionOffset;

        // Step 2: Compute the target world position
        Vector3 targetWorldPos = headBone.position + rotatedOffset;

        // Step 2b: a wall the head reaches into moves the whole visible body back, not just the
        // camera. Holding only the camera back left it above the torso, so looking down at a
        // wall looked into the chest; shifted together, the head, camera and chest keep their
        // usual layout. Only the armature moves: the player's position and collider stay put.
        bool hasAnchor = TryClipAnchor(targetWorldPos, out Vector3 anchor);
        if (hasAnchor)
        {
            Vector3 toTarget = targetWorldPos - anchor;
            float desiredDistance = toTarget.magnitude;
            if (desiredDistance > 0.0001f)
            {
                Vector3 direction = toTarget / desiredDistance;
                float clear = ProbeClearDistance(anchor, direction, desiredDistance);
                allowedDistance = clear < allowedDistance
                    ? clear
                    : Mathf.MoveTowards(allowedDistance, clear, clipReturnSpeed * Time.deltaTime);

                Vector3 shift = direction * (Mathf.Min(allowedDistance, desiredDistance) - desiredDistance);
                shift.y = 0f;
                SetBodyShift(shift);
                targetWorldPos += shift;
            }
        }

        // Step 3: Convert to camera parent local space
        Vector3 targetLocalPos = playerCamera.transform.parent.InverseTransformPoint(targetWorldPos);

        // Step 4: Smoothly move the camera
        Vector3 easedLocalPos = Vector3.Lerp(
            playerCamera.transform.localPosition,
            targetLocalPos,
            Time.deltaTime * adjustSpeed
        );

        // Step 5: the lerp leaves the camera trailing its target, so the wall limit is also
        // applied to where it actually renders from. Only the position is held back, never the
        // rotation, so looking down against a wall still works.
        Vector3 easedWorldPos = playerCamera.transform.parent.TransformPoint(easedLocalPos);
        if (hasAnchor)
        {
            Vector3 fromAnchor = easedWorldPos - anchor;
            float easedDistance = fromAnchor.magnitude;
            if (easedDistance > allowedDistance && easedDistance > 0.0001f)
                easedWorldPos = anchor + fromAnchor * (allowedDistance / easedDistance);
        }

        playerCamera.transform.localPosition =
            playerCamera.transform.parent.InverseTransformPoint(easedWorldPos);
    }

    /// <summary>
    /// Moves the armature sideways by <paramref name="worldShift"/> from its rest x/z, leaving
    /// its height to ArmatureIK, which settles it onto the ground before this runs.
    /// </summary>
    private void SetBodyShift(Vector3 worldShift)
    {
        if (ArmatureParent == null)
            return;
        Transform parent = ArmatureParent.parent;
        Vector3 localShift = parent != null ? parent.InverseTransformVector(worldShift) : worldShift;
        Vector3 local = ArmatureParent.localPosition;
        local.x = armatureRestLocalXZ.x + localShift.x;
        local.z = armatureRestLocalXZ.y + localShift.z;
        ArmatureParent.localPosition = local;
    }

    /// <summary>
    /// The probe's start: on the capsule's centre line at the target's height, kept within the
    /// capsule. That point is inside the body, which physics keeps out of scenery, and the probe
    /// from it to the head is short and sideways, so it never passes through a floor. It does not
    /// depend on where the camera is, so the clamp cannot feed back into it.
    /// </summary>
    private bool TryClipAnchor(Vector3 target, out Vector3 anchor)
    {
        if (bodyCollider == null)
            bodyCollider = playerCamera.transform.root.GetComponentInChildren<CapsuleCollider>();
        if (bodyCollider == null)
        {
            anchor = default;
            return false;
        }

        // Inset by the probe's radius only: the centre line is inside the capsule right up to its
        // top, and the capsule's top sits just above the head. Insetting by the capsule radius put
        // the anchor ~0.3 m under the camera, so a blocked camera slid down toward the chest.
        Bounds bounds = bodyCollider.bounds;
        float inset = Mathf.Min(clipRadius + clipSkin, bounds.extents.y);
        anchor = bounds.center;
        anchor.y = Mathf.Clamp(target.y, bounds.min.y + inset, bounds.max.y - inset);
        return true;
    }

    /// <summary>
    /// How far from <paramref name="anchor"/> along <paramref name="direction"/> the camera can
    /// sit without ending up inside something, up to <paramref name="maxDistance"/>. The
    /// player's own colliders are skipped by hierarchy, so no layer setup is needed.
    /// </summary>
    private float ProbeClearDistance(Vector3 anchor, Vector3 direction, float maxDistance)
    {
        int count = Physics.SphereCastNonAlloc(
            anchor,
            clipRadius,
            direction,
            hitBuffer,
            maxDistance,
            clipMask,
            QueryTriggerInteraction.Ignore
        );

        Transform self = playerCamera.transform.root;
        float nearest = maxDistance;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hitBuffer[i].collider;
            if (collider == null || collider.transform.IsChildOf(self))
                continue;
            float clear = hitBuffer[i].distance - clipSkin;
            if (clear < nearest)
                nearest = clear;
        }

        return Mathf.Max(0f, nearest);
    }
}
