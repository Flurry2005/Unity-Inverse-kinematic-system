using System;
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
    [SerializeField] private Transform rightFoot;
    [SerializeField] private Transform leftFoot;
    [SerializeField] private Transform armatureParent;

    [Header("Settings")]
    [SerializeField] private float rayYOffset = 1;
    [SerializeField] private float rayDistance = 0.1f;
    [SerializeField] private float AdjustSpeed;
    public bool UpdateHeight = true;
    [SerializeField] private bool manualUpdateToggle;

    [Header("LayerMasks")]
    [SerializeField] private LayerMask hitLayers;
    [SerializeField] private LayerMask ignoreLayers;

    [Header("Armature Live Values")]
    [SerializeField] private float previousArmatureParentYValue = 0;
    [SerializeField] private float originalArmatureParentYValue;
    [SerializeField] public float currentYoffset;
    [SerializeField] private float previousArmatureParentZRotation = 0;

    private void Start()
    {
        previousArmatureParentYValue = armatureParent.transform.position.y;
    }

    public bool UpdateArmature()
    {
        if (!manualUpdateToggle) return false;

        if (UsingNavMeshAgent) return UpdateArmatureForNavAgent();
        else if (UsingRigidbody) return UpdateArmatureForRigidbody();
        else return false;

    }

    private bool UpdateArmatureForRigidbody()
    {
        originalArmatureParentYValue = armatureParent.transform.position.y;

        float rayhitlowestPoint = 0f;

        float offsetRayDistance = rayDistance;
        offsetRayDistance += 0;

        //Zero offset and recalc later
        currentYoffset = 0;

        if (Physics.Raycast(rightFoot.position + new Vector3(0, rayYOffset, 0), Vector3.down, out var hitRight, offsetRayDistance, (hitLayers) & ~(1 << ignoreLayers))
            && UpdateHeight)
        {
            //Ensure armature is not offset on a Roof 
            if (hitRight.normal.y < 0)
            {
                previousArmatureParentYValue = originalArmatureParentYValue;
                return false;
            }
            rayhitlowestPoint = hitRight.point.y;
        }
        else
        {
            previousArmatureParentYValue = originalArmatureParentYValue;
            return false;
        }


        if (Physics.Raycast(leftFoot.position + new Vector3(0, rayYOffset, 0), Vector3.down, out var hitLeft, offsetRayDistance, (hitLayers) & ~(1 << ignoreLayers))
            && UpdateHeight)
        {
            //Ensure armature is not offset on a Roof 
            if (hitLeft.normal.y < 0)
            {
                previousArmatureParentYValue = originalArmatureParentYValue;
                return false;
            }

            if (hitLeft.point.y < rayhitlowestPoint)
                rayhitlowestPoint = hitLeft.point.y;
 

            Vector3 NewArmatureParentWorldPos = armatureParent.transform.position;

            NewArmatureParentWorldPos.y = Mathf.Lerp(previousArmatureParentYValue, rayhitlowestPoint, Time.deltaTime * AdjustSpeed);

            previousArmatureParentYValue = NewArmatureParentWorldPos.y;

            armatureParent.transform.position = NewArmatureParentWorldPos;

            //Calculate correct Y offset
            currentYoffset = rayhitlowestPoint - originalArmatureParentYValue;
        }
        else
        {
            previousArmatureParentYValue = originalArmatureParentYValue;
            return false;
        }

        return true;
    }

    private bool UpdateArmatureForNavAgent()
    {
        originalArmatureParentYValue = armatureParent.transform.position.y;

        float rayhitlowestPoint = 0f;

        float offsetRayDistance = rayDistance;
        offsetRayDistance += 0;

        //Zero offset and recalc later
        currentYoffset = 0;

        if (Physics.Raycast(rightFoot.position + new Vector3(0, rayYOffset, 0), Vector3.down, out var hitRight, offsetRayDistance, (hitLayers) & ~(1 << ignoreLayers))
            && UpdateHeight)
        {
            //Ensure armature is not offset on a Roof
            if (hitRight.normal.y < 0)
            {
                previousArmatureParentYValue = originalArmatureParentYValue;
                return false;
            }
            rayhitlowestPoint = hitRight.point.y;
        }
        else
        {
            previousArmatureParentYValue = originalArmatureParentYValue;
            return false;
        }


        if (Physics.Raycast(leftFoot.position + new Vector3(0, rayYOffset, 0), Vector3.down, out var hitLeft, offsetRayDistance, (hitLayers) & ~(1 << ignoreLayers))
            && UpdateHeight)
        {
            //Ensure armature is not offset on a Roof
            if (hitLeft.normal.y < 0)
            {
                previousArmatureParentYValue = originalArmatureParentYValue;
                return false;
            }

            if (hitLeft.point.y < rayhitlowestPoint)
                rayhitlowestPoint = hitLeft.point.y;
 
            Vector3 NewArmatureParentWorldPos = armatureParent.transform.position;

            NewArmatureParentWorldPos.y = Mathf.Lerp(previousArmatureParentYValue, rayhitlowestPoint, Time.deltaTime * AdjustSpeed);

            previousArmatureParentYValue = NewArmatureParentWorldPos.y;

            armatureParent.transform.position = NewArmatureParentWorldPos;

            //Calculate correct Y offset
            currentYoffset = rayhitlowestPoint - originalArmatureParentYValue;
        }
        else
        {
            previousArmatureParentYValue = originalArmatureParentYValue;
            return false;
        }

        return true;
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
