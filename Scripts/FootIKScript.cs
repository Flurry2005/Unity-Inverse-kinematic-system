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

    private Vector3 previousNoIkPos;

    private void LateUpdate()
    {
        footIK.weight = 0;
        footRefConstraint.weight = 1;

        if(playerColliderUpdater != null)
            playerColliderUpdater.UpdateCollider(ArmatureIKScript.UpdateArmature(), ArmatureIKScript.UsingNavMeshAgent, ArmatureIKScript.UsingRigidbody);

        if (CameraHeightAdjusterScript != null) CameraHeightAdjusterScript.AdjustHeight();

        transform.position = footRefConstraint.transform.position;

        rayOrigin = transform.position + Vector3.up * rayYOffset;
        var footPos = footRefConstraint.transform.position;

        if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, rayDistance, mask & ~(1 << playerLayer)))
        {
            if (hit.normal.y < 0) return;

            var IKpos = hit.point;
            IKpos.y += plantedYOffset - ArmatureIKScript.currentYoffset;

            var NoIkPos = footRefConstraint.transform.position;
            NoIkPos.y += ArmatureIKScript.currentYoffset;

            previousNoIkPos = NoIkPos;

            NoIkPos.y += (previousNoIkPos.y - NoIkPos.y) * (hit.normal.y);

            if (Mathf.Abs(NoIkPos.y - hit.point.y) <= ExitIKThreashhold)
            {
                IKTarget.position = Vector3.Lerp(IKTarget.position, IKpos, Time.deltaTime * ToIkSpeed);

            }
            else
            {
                IKTarget.position = Vector3.Lerp(IKTarget.position, NoIkPos, Time.deltaTime * ToNoIkSpeed);
            }
            footIK.weight = 1;

            var tarRot = Quaternion.Lerp(IKTarget.rotation, Quaternion.FromToRotation(Vector3.up, hit.normal) * footRefConstraint.transform.rotation, Time.deltaTime * IKFootRotationSpeed   );
            IKTarget.rotation = tarRot;

            if (IKpos.y <= footPos.y)
            {
                IKTarget.position = footRefConstraint.transform.position;
                return;
            }

        }
        Debug.DrawRay(rayOrigin, Vector3.down * rayDistance, Color.red);
    }
}