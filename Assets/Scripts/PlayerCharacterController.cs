using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterMovementController))]
[RequireComponent(typeof(AnimatorCache))]
public class PlayerCharacterController : MonoBehaviour
{
    private const float PickupApproachSameSideDotThreshold = 0.05f;

    public enum ActionState
    {
        Idle,
        Moving,
        PickupNavigating,
        PickupReaching
    }

    [Header("References")]
    [SerializeField] private CharacterMovementController movementController;
    [SerializeField] private AnimatorCache animatorCache;

    [Header("Animation")]
    [SerializeField] private string movingParameter = "isMoving";
    [SerializeField] private string speedParameter = "moveSpeed";
    [SerializeField] private string moveRightParameter = "moveRight";
    [SerializeField] private string moveForwardParameter = "moveForward";
    [SerializeField] private string pickupTrigger = "pickup";

    [Header("Pickup")]
    [SerializeField] private string pickupStateName = "Base Layer.Pickup";
    [SerializeField, Min(0f)] private float pickupReachTimeout = 3.5f;
    [SerializeField, Min(0.02f)] private float pickupApproachTolerance = 0.05f;
    [SerializeField, Min(1)] private int pickupApproachSamplesPerSector = 7;
    [SerializeField, Range(0f, 1f)] private float pickupAttachPrimeStartNormalizedTime = 0.18f;
    [SerializeField, Range(0f, 1f)] private float pickupAttachPrimeEndNormalizedTime = 0.92f;
    [SerializeField] private bool keepCharacterGroundedDuringPickup = true;

    private ActionState currentState;
    private PickableItem currentPickupItem;
    private Vector3 currentPickupApproachPosition;
    private bool hasPickupApproachPosition;
    private bool hasPickupAttached;
    private bool hasSeenPickupAnimationState;
    private float stateTime;
    private float pickupGroundY;

    private CharacterMovementController Movement => movementController
        ? movementController
        : movementController = GetComponent<CharacterMovementController>();

    private AnimatorCache AnimCache => animatorCache
        ? animatorCache
        : animatorCache = GetComponent<AnimatorCache>();

    private Animator AnimatorComponent => cachedAnimator ? cachedAnimator : cachedAnimator = GetComponent<Animator>();

    private Animator cachedAnimator;

    public ActionState CurrentState => currentState;
    public PickableItem CurrentPickupItem => currentPickupItem;
    public float CurrentStateTime => stateTime;
    public float PickupGroundY => pickupGroundY;
    public bool ShouldKeepGrounded => keepCharacterGroundedDuringPickup && IsPickupActive;
    public bool IsPickupActive => currentState is ActionState.PickupNavigating or ActionState.PickupReaching;
    public bool IsPickupReaching => currentState == ActionState.PickupReaching;
    public bool IsPickupAttachPrimed => currentState == ActionState.PickupReaching && IsPickupAttachWindowActive();
    public bool IsInputLocked => IsPickupActive;
    public bool IsPickupNavigating => currentState == ActionState.PickupNavigating;
    public Transform CharacterRoot => transform;

    private void Reset()
    {
        movementController = GetComponent<CharacterMovementController>();
        animatorCache = GetComponent<AnimatorCache>();
    }

    private void Update()
    {
        stateTime += Time.deltaTime;

        switch (currentState)
        {
            case ActionState.Idle:
            case ActionState.Moving:
                UpdateMovementState();
                break;
            case ActionState.PickupNavigating:
                TickPickupNavigating();
                break;
            case ActionState.PickupReaching:
                TickPickupReaching();
                break;
        }

        UpdateAnimator();
    }

    public CharacterMovementController.DestinationRequestResult TryResolveDestinationDetailed(Vector3 worldPosition, out Vector3 sampledPosition)
    {
        return Movement.TryResolveDestination(worldPosition, out sampledPosition);
    }

    public CharacterMovementController.DestinationRequestResult TryMoveToResolvedDestination(Vector3 resolvedPosition, out Vector3 destination)
    {
        if (IsInputLocked)
        {
            destination = resolvedPosition;
            return CharacterMovementController.DestinationRequestResult.AgentRejectedDestination;
        }

        var result = Movement.TrySetResolvedDestination(resolvedPosition, out destination);
        if (result == CharacterMovementController.DestinationRequestResult.Success)
            SetState(ActionState.Moving);

        return result;
    }

    public void StopMovement()
    {
        if (IsPickupActive)
            return;

        Movement.Stop();
        SetState(ActionState.Idle);
    }

    public bool RequestPickup(PickableItem item)
    {
        if (item == null || item.IsPickedUp || IsPickupActive)
            return false;

        currentPickupItem = item;
        hasPickupAttached = false;
        pickupGroundY = transform.position.y;

        if (HasReachedPickupStart(item))
        {
            BeginPickupReach();
            return true;
        }

        if (!TryMoveToPickupApproach(item))
        {
            currentPickupItem = null;
            SetState(ActionState.Idle);
            return false;
        }

        SetState(ActionState.PickupNavigating);
        return true;
    }

    public void NotifyPickupAttached()
    {
        if (currentState != ActionState.PickupReaching || currentPickupItem == null)
            return;

        hasPickupAttached = true;
        stateTime = 0f;
    }

    public void CancelPickup()
    {
        currentPickupItem = null;
        hasPickupApproachPosition = false;
        hasPickupAttached = false;
        hasSeenPickupAnimationState = false;
        SetState(Movement.HasPath || Movement.IsMoving ? ActionState.Moving : ActionState.Idle);
    }

    private void UpdateAnimator()
    {
        if (!AnimCache)
            return;

        var isLocomotingState = currentState is ActionState.Moving or ActionState.PickupNavigating;
        var moving = isLocomotingState && (Movement.HasPath || Movement.IsMoving);
        var moveSpeed = Movement.SmoothedSpeed;
        var moveDirection = Movement.SmoothedLocalMoveDirection;

        AnimCache.TrySet(movingParameter, moving);
        AnimCache.TrySet(speedParameter, moveSpeed);
        AnimCache.TrySet(moveRightParameter, moveDirection.x);
        AnimCache.TrySet(moveForwardParameter, moveDirection.y);
    }

    private void UpdateMovementState()
    {
        if (Movement.HasPath || Movement.IsMoving)
        {
            if (currentState != ActionState.Moving)
                SetState(ActionState.Moving);
            return;
        }

        if (currentState != ActionState.Idle)
            SetState(ActionState.Idle);
    }

    private void TickPickupNavigating()
    {
        if (currentPickupItem == null || currentPickupItem.IsPickedUp)
        {
            CancelPickup();
            return;
        }

        if (HasReachedPickupStart(currentPickupItem))
        {
            BeginPickupReach();
            return;
        }

        if (Movement.HasPath || Movement.IsMoving)
            return;

        if (TryMoveToPickupApproach(currentPickupItem))
            return;

        if (HasReachedPickupStart(currentPickupItem))
            BeginPickupReach();
        else
            CancelPickup();
    }

    private void BeginPickupReach()
    {
        Movement.Stop();
        hasPickupApproachPosition = false;
        hasSeenPickupAnimationState = false;
        pickupGroundY = transform.position.y;
        AnimCache?.TrySetTrigger(pickupTrigger);
        SetState(ActionState.PickupReaching);
    }

    private void TickPickupReaching()
    {
        if (currentPickupItem == null || currentPickupItem.IsPickedUp)
        {
            CancelPickup();
            return;
        }

        if (hasPickupAttached)
        {
            if (HasPickupAnimationFinished())
                CompletePickup();
            return;
        }

        if (stateTime >= pickupReachTimeout)
            CancelPickup();
    }

    private void CompletePickup()
    {
        if (currentPickupItem != null)
        {
            var collected = currentPickupItem;
            currentPickupItem = null;
            hasPickupApproachPosition = false;
            hasPickupAttached = false;
            collected.Collect();
            Destroy(collected.gameObject);
        }

        SetState(ActionState.Idle);
    }

    private bool IsPickupAnimationState(AnimatorStateInfo state)
    {
        if (state.IsName(pickupStateName))
            return true;

        var shortStateName = pickupStateName;
        var lastDotIndex = shortStateName.LastIndexOf('.');
        if (lastDotIndex >= 0 && lastDotIndex < shortStateName.Length - 1)
            shortStateName = shortStateName[(lastDotIndex + 1)..];

        return state.IsName(shortStateName);
    }

    private bool HasPickupAnimationFinished()
    {
        if (!AnimatorComponent || string.IsNullOrWhiteSpace(pickupStateName))
            return stateTime >= pickupReachTimeout * 0.5f;

        var state = AnimatorComponent.GetCurrentAnimatorStateInfo(0);
        var isCurrentPickupState = IsPickupAnimationState(state);
        var isTransitioning = AnimatorComponent.IsInTransition(0);
        var isNextPickupState = isTransitioning && IsPickupAnimationState(AnimatorComponent.GetNextAnimatorStateInfo(0));

        if (isCurrentPickupState || isNextPickupState)
        {
            hasSeenPickupAnimationState = true;
            return false;
        }

        return hasSeenPickupAnimationState;
    }

    private bool IsPickupAttachWindowActive()
    {
        if (!AnimatorComponent || string.IsNullOrWhiteSpace(pickupStateName))
            return stateTime >= 0.1f && stateTime <= pickupReachTimeout;

        if (!TryGetActivePickupAnimationState(out var pickupState))
            return false;

        var normalizedTime = pickupState.normalizedTime;
        return normalizedTime >= pickupAttachPrimeStartNormalizedTime &&
               normalizedTime <= pickupAttachPrimeEndNormalizedTime;
    }

    private bool TryGetActivePickupAnimationState(out AnimatorStateInfo pickupState)
    {
        pickupState = default;
        if (!AnimatorComponent)
            return false;

        var currentState = AnimatorComponent.GetCurrentAnimatorStateInfo(0);
        if (IsPickupAnimationState(currentState))
        {
            pickupState = currentState;
            return true;
        }

        if (!AnimatorComponent.IsInTransition(0))
            return false;

        var nextState = AnimatorComponent.GetNextAnimatorStateInfo(0);
        if (!IsPickupAnimationState(nextState))
            return false;

        pickupState = nextState;
        return true;
    }

    private bool TryMoveToPickupApproach(PickableItem item)
    {
        if (!TryCalculatePickupApproach(item, out var resolvedApproachPosition))
            return false;

        return TrySetPickupApproach(resolvedApproachPosition);
    }

    private bool TryCalculatePickupApproach(PickableItem item, out Vector3 approachPosition)
    {
        approachPosition = default;
        var preferredApproachPosition = item.GetApproachPosition(transform.position);
        var preferredApproachDirection = preferredApproachPosition - item.ApproachPivot.position;
        preferredApproachDirection.y = 0f;
        if (preferredApproachDirection.sqrMagnitude > 0.001f)
            preferredApproachDirection.Normalize();

        foreach (var candidate in item.GetApproachPositions(transform.position, pickupApproachSamplesPerSector))
        {
            var result = Movement.TryGetPathLength(candidate, out var sampledPosition, out _);
            if (result != CharacterMovementController.DestinationRequestResult.Success)
                continue;
            if (!IsPickupApproachOnPreferredSide(item, preferredApproachDirection, sampledPosition))
                continue;

            approachPosition = sampledPosition;
            return true;
        }

        if (Movement.TryGetPathLength(preferredApproachPosition, out var resolvedPreferredPosition, out _) ==
            CharacterMovementController.DestinationRequestResult.Success &&
            IsPickupApproachOnPreferredSide(item, preferredApproachDirection, resolvedPreferredPosition))
        {
            approachPosition = resolvedPreferredPosition;
            return true;
        }

        return false;
    }

    private static bool IsPickupApproachOnPreferredSide(
        PickableItem item,
        Vector3 preferredApproachDirection,
        Vector3 sampledPosition)
    {
        if (preferredApproachDirection.sqrMagnitude <= 0.001f)
            return true;

        var sampledDirection = sampledPosition - item.ApproachPivot.position;
        sampledDirection.y = 0f;
        if (sampledDirection.sqrMagnitude <= 0.001f)
            return false;

        sampledDirection.Normalize();
        return Vector3.Dot(preferredApproachDirection, sampledDirection) > PickupApproachSameSideDotThreshold;
    }

    private bool TrySetPickupApproach(Vector3 resolvedPosition)
    {
        // The pickup approach has already been resolved onto the NavMesh. Re-sampling it here
        // can snap to the opposite side of the item, which is the bug we want to avoid.
        var result = Movement.TrySetResolvedDestination(resolvedPosition, out currentPickupApproachPosition);
        if (result != CharacterMovementController.DestinationRequestResult.Success)
            return false;

        hasPickupApproachPosition = true;
        return true;
    }

    private bool HasReachedPickupStart(PickableItem item)
    {
        if (item == null)
            return false;

        var planarCharacterPosition = transform.position;
        planarCharacterPosition.y = 0f;
        var targetApproachPosition = hasPickupApproachPosition
            ? currentPickupApproachPosition
            : item.GetApproachPosition(transform.position);
        var planarApproachPosition = targetApproachPosition;
        planarApproachPosition.y = 0f;

        var approachTolerance = Mathf.Max(pickupApproachTolerance, Movement.AgentStoppingDistance + 0.05f);
        return Vector3.Distance(planarCharacterPosition, planarApproachPosition) <= approachTolerance;
    }

    private void SetState(ActionState newState)
    {
        currentState = newState;
        stateTime = 0f;
    }
}
