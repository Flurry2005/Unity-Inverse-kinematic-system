using System;
using Mirror.BouncyCastle.Asn1.Cmp;
using UnityEngine;
using UnityEngine.AI;

public class PlayerColliderUpdater : MonoBehaviour
{
    [Header("Player Collider")]
    [SerializeField] private CapsuleCollider PlayerCollider;

    [Header("Transforms")]
    [SerializeField] private Transform HeadPosition;
    [SerializeField] private NavMeshAgent navAgent;
    [SerializeField] private Transform LocalSpaceZero;

    [Header("Settings")]
    [SerializeField] private float rayYOffset = 1;
    [SerializeField] private float rayDistance = 0.1f;
    [SerializeField] private float additionalHeadHeight;
    [SerializeField] private float addtionalCenterAdjustment;
    [Tooltip("Ground found more than this far above the capsule's base is ignored and the capsule keeps " +
             "full height, so it bumps into a raised floor's edge instead of shrinking to fit under it.")]
    [SerializeField] private float maxGroundAboveBase = 0.5f;
    [SerializeField] private bool grounded;
    private float previousNavAgentBaseOffset;

    [Header("Layer Masks")]
    [SerializeField] private LayerMask GroundLayer;
    [SerializeField] private LayerMask LayersToIgnore;

    [Header("Script References")]
    [SerializeField] private PlayerMovement playerMovmentScript;

    // Update is called once per frame
    public void UpdateCollider(bool couldUpdateArmature, bool usingNavMeshAgent, bool usingRigidbody)
    {
        if (usingNavMeshAgent) UpdateColliderForNavAgent(couldUpdateArmature);
        if (usingRigidbody) UpdateColliderForRigidbody(couldUpdateArmature);
    }

    private void UpdateColliderForRigidbody(bool couldUpdateArmature)
    {
        if (!couldUpdateArmature) return;

        var rayOrigin = HeadPosition.position;
        rayOrigin.y += rayYOffset;

            if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, rayDistance, GroundLayer & ~LayersToIgnore.value) && playerMovmentScript.grounded)
            {
                float newColliderHeight = HeadPosition.position.y - hit.point.y + additionalHeadHeight;
                // The capsule starts at this transform, so ground found below it (a ray reaching the
                // terrain under a foundation) only made the top stick out above the head -- 2.4 m
                // above it, into door frame beams, which pushed the player down into the floor.
                float headAboveBase = HeadPosition.position.y - PlayerCollider.transform.position.y + additionalHeadHeight;
                newColliderHeight = Mathf.Min(newColliderHeight, headAboveBase);
                // Ground well above the feet is a raised floor the head has already reached over
                // (walking from the terrain into a foundation's edge): sizing to it shrank the
                // capsule to fit the gap under the slab and the player walked in underneath.
                if (hit.point.y - PlayerCollider.transform.position.y > maxGroundAboveBase)
                    newColliderHeight = headAboveBase;

                PlayerCollider.height = newColliderHeight;
                PlayerCollider.center = new Vector3(0, newColliderHeight / 2f, 0);

                playerMovmentScript.playerHeight = newColliderHeight;
            }
    }

    private void UpdateColliderForNavAgent(bool couldUpdateArmature)
    {
         if (!couldUpdateArmature)
        {    
            navAgent.baseOffset = Mathf.Lerp(previousNavAgentBaseOffset, 0, Time.deltaTime * 5f);
            previousNavAgentBaseOffset = navAgent.baseOffset;
            return;
        }

        var rayOrigin = HeadPosition.position;
        rayOrigin.y += rayYOffset;

        if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, rayDistance, GroundLayer & ~LayersToIgnore.value) && (this.grounded))
        {
            float newColliderHeight = rayOrigin.y - hit.point.y;
            newColliderHeight += additionalHeadHeight;

            Debug.DrawLine(rayOrigin, hit.point, Color.green);

            PlayerCollider.height = newColliderHeight;
            PlayerCollider.center = new Vector3(0, (newColliderHeight) / 2f, 0);

            navAgent.baseOffset = 0f;

            if (Mathf.Abs(LocalSpaceZero.position.y - hit.point.y) > 0)
            {
                navAgent.baseOffset = Mathf.Lerp(previousNavAgentBaseOffset, hit.point.y - LocalSpaceZero.position.y, Time.deltaTime * 20f);
            }
            else
                navAgent.baseOffset = Mathf.Lerp(previousNavAgentBaseOffset, 0f, Time.deltaTime * 20f);

            previousNavAgentBaseOffset = navAgent.baseOffset;
        }
    }
}
