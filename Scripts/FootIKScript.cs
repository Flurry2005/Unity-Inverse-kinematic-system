using UnityEngine;
using UnityEngine.Animations.Rigging;

public class FootIKScript : MonoBehaviour
{
    [Header("Constraints")]
    [SerializeField] private MultiParentConstraint footRefConstraint;
    [SerializeField] private TwoBoneIKConstraint footIK;

    [Header("Transforms")]
    [SerializeField] private Transform IKTarget;

    [Header("Settings")]
    [SerializeField] private float rayYOffset = 1;
    [SerializeField] private float rayDistance = 0.1f;

    [Tooltip("Cosmetic clearance between the ground and the foot target, to lift the sole off the surface. Purely visual: it is added to the target position only, and never takes part in any decision.")]
    [SerializeField] private float plantedYOffset = 0.1f;

    [SerializeField] private float ExitIKThreashhold;
    [SerializeField] private float ToIkSpeed;
    [SerializeField] private float ToNoIkSpeed;
    [SerializeField] private float IKFootRotationSpeed;
    [SerializeField] private bool UpdateArmature;

    [Header("LayerMasks")]
    [SerializeField] private LayerMask mask;
    [SerializeField] private LayerMask playerLayer;

    [Header("ScriptReferences")]
    [SerializeField] private PlayerColliderUpdater playerColliderUpdater;
    [SerializeField] private CameraHeightAdjuster CameraHeightAdjusterScript;
    [SerializeField] private ArmatureIK ArmatureIKScript;

    private Vector3 rayOrigin;

    private void LateUpdate()
    {
        footIK.weight = 0;
        footRefConstraint.weight = 1;

        // Did the armature actually settle onto the ground this frame? It returns false when
        // either foot ray missed, when a ray hit an underside, or when its manual toggle is
        // off -- in any of those cases the body was NOT lowered onto the terrain, so no foot is
        // really standing on it.
        //
        // Hoisted out of the null check below: this used to be called as an argument to
        // UpdateCollider, so leaving playerColliderUpdater unassigned silently stopped the
        // armature adjusting at all.
        bool armatureAdjusted = ArmatureIKScript.UpdateArmature();

        if (playerColliderUpdater != null)
            playerColliderUpdater.UpdateCollider(armatureAdjusted, ArmatureIKScript.UsingNavMeshAgent, ArmatureIKScript.UsingRigidbody);

        if (CameraHeightAdjusterScript != null) CameraHeightAdjusterScript.AdjustHeight();

        transform.position = footRefConstraint.transform.position;

        rayOrigin = transform.position + Vector3.up * rayYOffset;

        // rayDistance is how far BELOW the foot to look for ground; the lift is added on top of
        // it. It used to be the whole length of the ray measured from the raised origin, so
        // raising rayYOffset silently ate into the reach -- a lift of 1 with a distance of 1.61
        // only ever probed 0.61 below the foot, and anything further down was missed entirely.
        float probeLength = rayYOffset + rayDistance;

        // playerLayer is a LayerMask, so its value is ALREADY a bitmask. The old
        // "1 << playerLayer" treated that value as a layer index, and C# masks a shift count to
        // five bits -- a player on layer 8 (value 256) shifted by 256 & 31 == 0, excluding
        // layer 0 instead of the player.
        int probeMask = mask & ~playerLayer.value;

        if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, probeLength, probeMask))
        {
            if (hit.normal.y < 0) return;

            // Where the foot rests ON the ground. plantedYOffset is deliberately not part of
            // this: it is cosmetic clearance, so it is added only to the position handed to
            // IKTarget and cannot shift any of the decisions below. It used to be baked in
            // here, which let it flip the "never go below the animation" test and drive the
            // foot through the terrain on a slope.
            var groundPos = hit.point;
            groundPos.y -= ArmatureIKScript.currentYoffset;

            var NoIkPos = footRefConstraint.transform.position;
            NoIkPos.y += ArmatureIKScript.currentYoffset;

            // No veto on lowering the foot. There used to be a "groundPos.y <= footPos.y ->
            // give up" test here, which meant the IK could only ever lift a foot: standing on
            // a downward slope, the ground is BELOW the animated pose, so the test fired every
            // frame and left the foot hanging in the air over the edge. How far the foot may
            // travel to reach the ground is ExitIKThreashhold's job, in both directions -- it
            // is already an absolute difference -- so one control is enough.
            // Planted means two things, not one: the ground is close enough to reach, AND the
            // armature actually came down to meet it. If the body never settled, the foot is
            // not standing on the terrain however near the surface happens to be.
            bool withinReach = Mathf.Abs(NoIkPos.y - hit.point.y) <= ExitIKThreashhold;
            bool planted = armatureAdjusted && withinReach;

            Vector3 target;
            float speed;
            if (planted)
            {
                // Close enough to plant: stand on the ground, plus the cosmetic clearance.
                target = groundPos;
                target.y += plantedYOffset;
                speed = ToIkSpeed;
            }
            else
            {
                // Too far off the ground to plant, so follow the animation -- but never below
                // the ground the ray just found. That unclamped position is what used to drive
                // the foot through the terrain, and the old veto was papering over it.
                //
                // The floor includes plantedYOffset, the same clearance the planted branch
                // uses. Without it a foot caught by this clamp sat plantedYOffset lower than
                // the same foot standing on the same ground, so the two feet disagreed about
                // where the surface was.
                target = NoIkPos;
                float floorY = groundPos.y + plantedYOffset;
                if (target.y < floorY)
                    target.y = floorY;
                speed = ToNoIkSpeed;
            }

            IKTarget.position = Vector3.Lerp(IKTarget.position, target, Time.deltaTime * speed);
            footIK.weight = 1;

            // Roll the sole onto the surface in every case except one: the body never settled
            // onto the terrain AND this foot is out of reach of the ground. Then it is not on
            // the terrain in any sense, so it keeps the animation's own orientation rather than
            // being twisted onto a surface it is nowhere near. Either condition on its own is
            // enough to align it.
            Quaternion groundAligned =
                Quaternion.FromToRotation(Vector3.up, hit.normal) * footRefConstraint.transform.rotation;

            IKTarget.rotation = Quaternion.Lerp(
                IKTarget.rotation,
                armatureAdjusted || withinReach ? groundAligned : footRefConstraint.transform.rotation,
                Time.deltaTime * IKFootRotationSpeed);
        }

        Debug.DrawRay(rayOrigin, Vector3.down * probeLength, Color.red);
    }
}
