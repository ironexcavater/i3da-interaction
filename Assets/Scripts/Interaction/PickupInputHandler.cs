using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Detects mouse clicks on PickableItem objects and delegates the pickup request
/// to PlayerCharacterController. Uses a dedicated physics layer so it never conflicts
/// with NavMeshPointClickInput (which targets movement/ground layers only).
/// </summary>
[DisallowMultipleComponent]
public class PickupInputHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private PlayerCharacterController characterController;

    [Header("Input")]
    [SerializeField] private InputActionReference pointerPositionAction;
    [SerializeField] private InputActionReference clickAction;

    [Header("Raycast")]
    [SerializeField] private LayerMask pickableLayer;
    [SerializeField, Min(0f)] private float rayDistance = 500f;
    [SerializeField] private bool ignoreClicksOverUi = true;

    private Camera SceneCamera => worldCamera
        ? worldCamera
        : worldCamera = Camera.main;

    private PlayerCharacterController Character => characterController
        ? characterController
        : characterController = FindFirstObjectByType<PlayerCharacterController>(FindObjectsInactive.Exclude);

    private void Reset()
    {
        worldCamera = Camera.main;
        characterController = FindFirstObjectByType<PlayerCharacterController>(FindObjectsInactive.Exclude);
    }

    private void OnEnable()
    {
        pointerPositionAction?.action?.Enable();
        clickAction?.action?.Enable();
    }

    private void OnDisable()
    {
        clickAction?.action?.Disable();
        pointerPositionAction?.action?.Disable();
    }

    private void Update()
    {
        if (clickAction == null || clickAction.action == null) return;
        if (!clickAction.action.WasPressedThisFrame()) return;

        if (ignoreClicksOverUi && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        var camera = SceneCamera;
        if (!camera) return;

        if (!PointerWorldUtility.TryGetPointerRay(camera, pointerPositionAction, out var ray, out _))
            return;

        if (!Physics.Raycast(ray, out var hit, rayDistance, pickableLayer, QueryTriggerInteraction.Collide))
            return;

        // Walk up the hierarchy in case the collider is on a child object
        var item = hit.collider.GetComponentInParent<PickableItem>();
        if (item == null || item.IsPickedUp) return;

        Character?.RequestPickup(item);
    }
}
