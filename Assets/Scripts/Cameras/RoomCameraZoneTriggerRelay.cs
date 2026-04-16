using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("")]
[RequireComponent(typeof(Collider))]
public class RoomCameraZoneTriggerRelay : MonoBehaviour
{
    [SerializeField] private RoomCameraZone zone;

    public void SetZone(RoomCameraZone roomCameraZone)
    {
        zone = roomCameraZone;
    }

    private void Reset()
    {
        zone = GetComponentInParent<RoomCameraZone>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!Application.isPlaying)
            return;

        zone?.NotifyTriggerEnter(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!Application.isPlaying)
            return;

        zone?.NotifyTriggerExit(other);
    }
}
