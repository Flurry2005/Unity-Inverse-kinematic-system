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

            if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, rayDistance, GroundLayer & ~(1 << LayersToIgnore)) && playerMovmentScript.grounded)
            {
                float newColliderHeight = HeadPosition.position.y - hit.point.y + additionalHeadHeight;

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

        if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, rayDistance, GroundLayer & ~(1 << LayersToIgnore)) && (this.grounded))
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
