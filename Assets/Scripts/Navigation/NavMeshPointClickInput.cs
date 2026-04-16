using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class NavMeshPointClickInput : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private PlayerCharacterController characterController;

    [Header("Input")]
    [SerializeField] private InputActionReference pointerPositionAction;
    [SerializeField] private InputActionReference clickAction;
    [SerializeField] private InputActionReference stopAction;

    [Header("Raycast")]
    [SerializeField] private LayerMask raycastLayers = Physics.DefaultRaycastLayers;
    [SerializeField] private LayerMask blockingLayers;
    [SerializeField, Min(0f)] private float rayDistance = 500f;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
    [SerializeField] private bool ignoreClicksOverUi = true;

    [Header("Debug")]
    [SerializeField] private bool logClickResults = true;

    private Camera SceneCamera => worldCamera ? worldCamera : worldCamera = Camera.main;
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
        pointerPositionAction.SetEnabled(true);
        clickAction.SetEnabled(true);
        stopAction.SetEnabled(true);
        ValidateConfiguration();
    }

    private void OnDisable()
    {
        stopAction.SetEnabled(false);
        clickAction.SetEnabled(false);
        pointerPositionAction.SetEnabled(false);
    }

    private void OnValidate()
    {
        rayDistance = Mathf.Max(0f, rayDistance);
        if (!worldCamera) worldCamera = Camera.main;
    }

    private void Update()
    {
        var character = Character;
        if (character != null && character.IsInputLocked)
            return;

        if (WasPressedThisFrame(stopAction))
        {
            character?.StopMovement();
            Log("Stopped current path.");
        }

        if (!WasPressedThisFrame(clickAction)) return;
        if (ignoreClicksOverUi && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            Log("Ignored click because the pointer is over UI.");
            return;
        }

        var camera = SceneCamera;
        if (!camera)
        {
            LogWarning("Point click failed because no world camera was found.");
            return;
        }

        if (!character)
        {
            LogWarning("Point click failed because no PlayerCharacterController was found.");
            return;
        }

        if (character.IsInputLocked)
            return;

        if (!PointerWorldUtility.TryGetPointerRay(camera, pointerPositionAction, out var ray, out var pointerPosition))
        {
            LogWarning("Point click failed because the pointer position action could not be read.");
            return;
        }
        if (IsPickableClick(ray, character))
            return;

        if (!TryResolveClickDestination(ray, character, out var clickPoint, out var resolvedPosition, out var usedPlaneFallback, out var resolveFailure, out var sampledPosition, out var blockingHit))
        {
            if (blockingHit.collider)
                Log($"Point click was blocked by {blockingHit.collider.name} on layer {blockingHit.collider.gameObject.layer}.");
            else if (resolveFailure == CharacterMovementController.DestinationRequestResult.AgentNotOnNavMesh)
                LogWarning("Point click failed because the character is not on a NavMesh and could not snap to one.");
            else if (resolveFailure == CharacterMovementController.DestinationRequestResult.ClickPointNotOnNavMesh)
                LogWarning($"Point click failed because no valid NavMesh destination was found for the click. ClickPoint={clickPoint}, SampledPosition={sampledPosition}.");
            else
                Log($"Point click raycast missed. ScreenPosition={pointerPosition}, Layers={raycastLayers.value}, Distance={rayDistance}.");

            return;
        }

        var result = character.TryMoveToResolvedDestination(resolvedPosition, out var destination);
        switch (result)
        {
            case CharacterMovementController.DestinationRequestResult.Success:
                var source = usedPlaneFallback ? "NavigationPlane" : "Raycast";
                Log($"Point click succeeded. ClickPoint={clickPoint}, Destination={destination}, Source={source}.");
                break;

            case CharacterMovementController.DestinationRequestResult.AgentNotOnNavMesh:
                LogWarning("Point click failed because the character is not on a NavMesh and could not snap to one.");
                break;

            case CharacterMovementController.DestinationRequestResult.ClickPointNotOnNavMesh:
                LogWarning($"Point click failed because the clicked position is not on the NavMesh. ClickPoint={clickPoint}, SampledPosition={sampledPosition}.");
                break;

            case CharacterMovementController.DestinationRequestResult.AgentRejectedDestination:
                LogWarning($"Point click failed because NavMeshAgent rejected the destination. SampledPosition={sampledPosition}.");
                break;
        }
    }

    private bool IsPickableClick(Ray ray, PlayerCharacterController character)
    {
        var hits = PointerWorldUtility.GetSortedHits(ray, rayDistance, Physics.DefaultRaycastLayers, triggerInteraction);
        if (hits == null || hits.Length == 0)
            return false;

        for (var i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (IsCharacterHit(hit.collider, character))
                continue;

            return hit.collider.GetComponentInParent<PickableItem>() != null;
        }

        return false;
    }
    private static bool WasPressedThisFrame(InputActionReference actionReference)
    {
        return actionReference != null
               && actionReference.action != null
               && actionReference.action.WasPressedThisFrame();
    }

    private void ValidateConfiguration()
    {
        if (!pointerPositionAction || pointerPositionAction.action == null)
            LogWarning("Point click is missing a pointer position InputActionReference.");

        if (!clickAction || clickAction.action == null)
            LogWarning("Point click is missing a click InputActionReference.");

        if (!stopAction || stopAction.action == null)
            LogWarning("Point click is missing a stop InputActionReference.");

        if (!SceneCamera)
            LogWarning("Point click could not find a world camera. Assign one explicitly or tag a camera as MainCamera.");

        if (!Character)
            LogWarning("Point click could not find a PlayerCharacterController in the scene.");

        if (raycastLayers.value == 0)
            LogWarning("Point click raycast layers are empty, so clicks can never hit the world.");

        if ((raycastLayers.value & blockingLayers.value) != 0)
            LogWarning("Point click destination layers and blocking layers overlap. Blocking layers should be separate from valid click surfaces.");
    }

    private bool TryResolveClickDestination(
        Ray ray,
        PlayerCharacterController character,
        out Vector3 clickPoint,
        out Vector3 resolvedPosition,
        out bool usedPlaneFallback,
        out CharacterMovementController.DestinationRequestResult resolveFailure,
        out Vector3 sampledPosition,
        out RaycastHit blockingHit)
    {
        clickPoint = default;
        resolvedPosition = default;
        usedPlaneFallback = false;
        resolveFailure = CharacterMovementController.DestinationRequestResult.Success;
        sampledPosition = default;
        blockingHit = default;

        var interactionLayers = raycastLayers.value | blockingLayers.value;
        if (interactionLayers == 0)
            return false;

        var hits = PointerWorldUtility.GetSortedHits(ray, rayDistance, interactionLayers, triggerInteraction);
        var foundDestinationHit = false;
        var foundResolvedHit = false;
        var bestVerticalDelta = float.PositiveInfinity;
        var bestDistance = float.PositiveInfinity;
        if (hits != null && hits.Length > 0)
        {
            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (IsCharacterHit(hit.collider, character))
                    continue;

                var hitLayer = 1 << hit.collider.gameObject.layer;

                if ((blockingLayers.value & hitLayer) != 0)
                {
                    if (foundResolvedHit)
                        break;

                    blockingHit = hit;
                    return false;
                }

                if ((raycastLayers.value & hitLayer) == 0)
                    continue;

                foundDestinationHit = true;

                var candidateResult = character.TryResolveDestinationDetailed(hit.point, out var candidateSampledPosition);
                switch (candidateResult)
                {
                    case CharacterMovementController.DestinationRequestResult.Success:
                        var verticalDelta = Mathf.Abs(hit.point.y - candidateSampledPosition.y);
                        if (!foundResolvedHit)
                        {
                            bestDistance = hit.distance;
                            bestVerticalDelta = verticalDelta;
                            clickPoint = hit.point;
                            resolvedPosition = candidateSampledPosition;
                            sampledPosition = candidateSampledPosition;
                            foundResolvedHit = true;
                            continue;
                        }

                        if (verticalDelta > bestVerticalDelta)
                            continue;
                        if (Mathf.Approximately(verticalDelta, bestVerticalDelta) && hit.distance >= bestDistance)
                            continue;

                        bestDistance = hit.distance;
                        bestVerticalDelta = verticalDelta;
                        clickPoint = hit.point;
                        resolvedPosition = candidateSampledPosition;
                        sampledPosition = candidateSampledPosition;
                        foundResolvedHit = true;
                        continue;

                    case CharacterMovementController.DestinationRequestResult.AgentNotOnNavMesh:
                        resolveFailure = candidateResult;
                        sampledPosition = candidateSampledPosition;
                        return false;

                    case CharacterMovementController.DestinationRequestResult.ClickPointNotOnNavMesh:
                        resolveFailure = candidateResult;
                        sampledPosition = candidateSampledPosition;
                        continue;
                }
            }
        }

        if (foundResolvedHit)
            return true;
        if (foundDestinationHit)
            return false;

        if (!TryGetNavigationPlanePoint(ray, character, out var planePoint))
            return false;

        clickPoint = planePoint;
        var planeResult = character.TryResolveDestinationDetailed(planePoint, out sampledPosition);
        if (planeResult == CharacterMovementController.DestinationRequestResult.Success)
        {
            resolvedPosition = sampledPosition;
            usedPlaneFallback = true;
            return true;
        }

        resolveFailure = planeResult;
        return false;
    }

    private static bool TryGetNavigationPlanePoint(Ray ray, PlayerCharacterController character, out Vector3 planePoint)
    {
        planePoint = default;

        var navigationPlane = new Plane(Vector3.up, character.transform.position);
        if (!navigationPlane.Raycast(ray, out var distance) || distance < 0f)
            return false;

        planePoint = ray.GetPoint(distance);
        return true;
    }

    private static bool IsCharacterHit(Collider collider, PlayerCharacterController character)
    {
        if (!collider || !character)
            return false;

        return collider.transform.root == character.CharacterRoot.root;
    }

    private void Log(string message)
    {
        if (!logClickResults) return;
        Debug.Log(message, this);
    }

    private void LogWarning(string message)
    {
        if (!logClickResults) return;
        Debug.LogWarning(message, this);
    }
}
