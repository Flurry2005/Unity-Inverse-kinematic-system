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

        // Step 3: Convert to camera parent local space
        Vector3 targetLocalPos = playerCamera.transform.parent.InverseTransformPoint(targetWorldPos);

        // Step 4: Smoothly move the camera
        playerCamera.transform.localPosition = Vector3.Lerp(
            playerCamera.transform.localPosition,
            targetLocalPos,
            Time.deltaTime * adjustSpeed
        );

    }
}
