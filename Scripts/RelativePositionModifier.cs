using UnityEngine;

public class RelativePositionModifer : MonoBehaviour
{
    [Header("Transforms")]
    public Transform ReferenceTransform;
    public Transform Target;  // This is the target the LookAt constraint is using

    [Header("Settings")]
    public float distanceForward = 5f;
    public float HeightOffset;

    void LateUpdate()
    {
        if (ReferenceTransform != null && Target != null)
        {
            Target.position = ReferenceTransform.position + transform.root.forward + ReferenceTransform.forward * distanceForward - new Vector3(0,HeightOffset,0);
        }
    }
}
