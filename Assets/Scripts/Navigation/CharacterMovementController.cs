using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class CharacterMovementController : MonoBehaviour
{
    private const float DirectionDeadzone = 0.0001f;

    public enum DestinationRequestResult
    {
        Success = 0,
        AgentNotOnNavMesh = 1,
        ClickPointNotOnNavMesh = 2,
        AgentRejectedDestination = 3
    }

    private enum LocalBasisDirection
    {
        Forward = 0,
        Backward = 1,
        Right = 2,
        Left = 3
    }

    [Header("Navigation")]
    [SerializeField, Min(0f)] private float navMeshSampleDistance = 1.5f;
    [SerializeField, Min(0f)] private float navMeshSnapDistance = 2f;
    [SerializeField, Min(0f)] private float projectedClickSampleDistance = 2.5f;
    [SerializeField, Min(0f)] private float destinationReachedThreshold = 0.05f;
    [SerializeField, Min(0f)] private float destinationReachedVelocityThreshold = 0.08f;
    [SerializeField, Min(0f)] private float destinationReachedStableTime = 0.08f;
    [SerializeField] private bool snapToNavMeshOnEnable = true;
    [SerializeField] private bool projectClicksToCurrentHeight = true;

    [Header("Direction Basis")]
    [SerializeField] private Transform directionReference;
    [SerializeField] private LocalBasisDirection forwardDirection = LocalBasisDirection.Forward;
    [SerializeField] private LocalBasisDirection rightDirection = LocalBasisDirection.Right;

    private NavMeshAgent navMeshAgent;
    private Rigidbody rigidbodyComponent;
    private NavMeshPath destinationPath;
    private float smoothedSpeed;
    private Vector2 smoothedLocalMoveDirection;
    private Vector3 smoothedWorldMoveDirection;
    private Vector3 lastPosition;
    private float destinationArrivalTimer;
    private bool hasReachedDestination;

    private NavMeshAgent Agent => navMeshAgent ? navMeshAgent : navMeshAgent = GetComponent<NavMeshAgent>();
    private Rigidbody Body => rigidbodyComponent ? rigidbodyComponent : rigidbodyComponent = GetComponent<Rigidbody>();
    private NavMeshPath DestinationPath => destinationPath ??= new NavMeshPath();
    private Transform DirectionReference => directionReference ? directionReference : transform;

    public bool HasPath => Agent.hasPath && Agent.pathStatus != NavMeshPathStatus.PathInvalid;
    public bool IsMoving => smoothedSpeed > 0.05f;
    public bool HasReachedDestination => hasReachedDestination;
    public Vector3 Destination => Agent.destination;
    public float SmoothedSpeed => smoothedSpeed;
    public Vector2 SmoothedLocalMoveDirection => smoothedLocalMoveDirection;
    public Vector3 SmoothedWorldMoveDirection => smoothedWorldMoveDirection;
    public float AgentStoppingDistance => Agent.stoppingDistance;
    public Vector3 FacingWorldForward => GetConfiguredPlanarDirection(forwardDirection);
    public Vector3 FacingWorldRight => GetConfiguredPlanarDirection(rightDirection);

    private void Awake()
    {
        lastPosition = transform.position;
        RefreshMotion(true);
    }

    private void OnValidate()
    {
        navMeshSampleDistance = Mathf.Max(0f, navMeshSampleDistance);
        navMeshSnapDistance = Mathf.Max(0f, navMeshSnapDistance);
        projectedClickSampleDistance = Mathf.Max(0f, projectedClickSampleDistance);
        destinationReachedThreshold = Mathf.Max(0f, destinationReachedThreshold);
        destinationReachedVelocityThreshold = Mathf.Max(0f, destinationReachedVelocityThreshold);
        destinationReachedStableTime = Mathf.Max(0f, destinationReachedStableTime);
    }

    private void OnEnable()
    {
        lastPosition = transform.position;

        if (snapToNavMeshOnEnable)
            TrySnapToNavMesh();

        RefreshMotion(true);
    }

    private void OnDisable()
    {
        if (navMeshAgent && Agent.isOnNavMesh)
        {
            Agent.isStopped = true;
            Agent.ResetPath();
        }

        hasReachedDestination = false;
        destinationArrivalTimer = 0f;
        smoothedSpeed = 0f;
        smoothedLocalMoveDirection = Vector2.zero;
        smoothedWorldMoveDirection = transform.forward;
    }

    private void Update()
    {
        RefreshMotion(false);
        UpdateArrivalState();
    }

    public bool TrySetDestination(Vector3 worldPosition)
    {
        return TrySetDestination(worldPosition, out _, out _) == DestinationRequestResult.Success;
    }

    public DestinationRequestResult TrySetDestination(Vector3 worldPosition, out Vector3 destination, out Vector3 sampledPosition)
    {
        destination = worldPosition;
        sampledPosition = worldPosition;

        var resolveResult = TryResolveDestination(worldPosition, out sampledPosition);
        if (resolveResult != DestinationRequestResult.Success)
            return resolveResult;

        return TrySetResolvedDestination(sampledPosition, out destination);
    }

    public DestinationRequestResult TryResolveDestination(Vector3 worldPosition, out Vector3 sampledPosition)
    {
        sampledPosition = worldPosition;

        var agent = Agent;
        if (!agent.isOnNavMesh && !TrySnapToNavMesh())
            return DestinationRequestResult.AgentNotOnNavMesh;

        if (TrySampleDestination(worldPosition, navMeshSampleDistance, out var hit))
        {
            sampledPosition = hit.position;
            return DestinationRequestResult.Success;
        }

        if (!projectClicksToCurrentHeight)
            return DestinationRequestResult.ClickPointNotOnNavMesh;

        var projectedPoint = worldPosition;
        projectedPoint.y = agent.nextPosition.y;
        var projectedSampleDistance = Mathf.Max(navMeshSampleDistance, projectedClickSampleDistance);
        if (!TrySampleDestination(projectedPoint, projectedSampleDistance, out hit))
            return DestinationRequestResult.ClickPointNotOnNavMesh;

        sampledPosition = hit.position;
        return DestinationRequestResult.Success;
    }

    public DestinationRequestResult TryGetPathLength(Vector3 worldPosition, out Vector3 sampledPosition, out float pathLength)
    {
        sampledPosition = worldPosition;
        pathLength = 0f;

        var resolveResult = TryResolveDestination(worldPosition, out sampledPosition);
        if (resolveResult != DestinationRequestResult.Success)
            return resolveResult;

        var agent = Agent;
        if (!agent.isOnNavMesh && !TrySnapToNavMesh())
            return DestinationRequestResult.AgentNotOnNavMesh;
        if (!agent.CalculatePath(sampledPosition, DestinationPath))
            return DestinationRequestResult.AgentRejectedDestination;
        if (DestinationPath.status != NavMeshPathStatus.PathComplete)
            return DestinationRequestResult.AgentRejectedDestination;

        pathLength = CalculatePathLength(DestinationPath);
        return DestinationRequestResult.Success;
    }

    public DestinationRequestResult TrySetResolvedDestination(Vector3 resolvedPosition, out Vector3 destination)
    {
        destination = resolvedPosition;

        var agent = Agent;
        if (!agent.isOnNavMesh && !TrySnapToNavMesh())
            return DestinationRequestResult.AgentNotOnNavMesh;
        if (!agent.CalculatePath(resolvedPosition, DestinationPath))
            return DestinationRequestResult.AgentRejectedDestination;
        if (DestinationPath.status != NavMeshPathStatus.PathComplete)
            return DestinationRequestResult.AgentRejectedDestination;
        if (!agent.SetPath(DestinationPath))
            return DestinationRequestResult.AgentRejectedDestination;

        destination = resolvedPosition;
        agent.isStopped = false;
        hasReachedDestination = false;
        destinationArrivalTimer = 0f;
        return DestinationRequestResult.Success;
    }

    public void Stop()
    {
        var agent = Agent;
        if (!agent.isOnNavMesh)
            return;

        agent.isStopped = true;
        agent.ResetPath();
        hasReachedDestination = true;
        destinationArrivalTimer = 0f;
        RefreshMotion(false);
    }

    public void SetAgentRotationControl(bool enabled)
    {
        var agent = Agent;
        if (agent)
            agent.updateRotation = enabled;
    }

    public bool TrySnapToNavMesh()
    {
        var agent = Agent;
        if (agent.isOnNavMesh)
            return true;
        if (!NavMesh.SamplePosition(transform.position, out var hit, navMeshSnapDistance, agent.areaMask))
            return false;
        return agent.Warp(hit.position);
    }

    private bool TrySampleDestination(Vector3 sampleOrigin, float sampleDistance, out NavMeshHit hit)
    {
        return NavMesh.SamplePosition(sampleOrigin, out hit, sampleDistance, Agent.areaMask);
    }

    private static float CalculatePathLength(NavMeshPath path)
    {
        if (path == null || path.corners == null || path.corners.Length < 2)
            return 0f;

        var total = 0f;
        for (var i = 1; i < path.corners.Length; i++)
            total += Vector3.Distance(path.corners[i - 1], path.corners[i]);

        return total;
    }

    private void RefreshMotion(bool immediate)
    {
        var agent = Agent;
        if (!agent.isOnNavMesh)
        {
            hasReachedDestination = false;
            destinationArrivalTimer = 0f;
            smoothedSpeed = 0f;
            smoothedLocalMoveDirection = Vector2.zero;
            smoothedWorldMoveDirection = transform.forward;
            lastPosition = transform.position;
            return;
        }

        var actualVelocity = GetPlanarVelocity();
        var targetSpeed = actualVelocity.magnitude;
        var targetLocalMoveDirection = GetLocalMoveDirection(actualVelocity);

        smoothedSpeed = immediate ? targetSpeed : Mathf.MoveTowards(smoothedSpeed, targetSpeed, 10f * Time.deltaTime);
        smoothedLocalMoveDirection = immediate
            ? targetLocalMoveDirection
            : Vector2.MoveTowards(smoothedLocalMoveDirection, targetLocalMoveDirection, 10f * Time.deltaTime);

        if (actualVelocity.sqrMagnitude > DirectionDeadzone)
        {
            var targetDirection = actualVelocity.normalized;
            smoothedWorldMoveDirection = immediate
                ? targetDirection
                : Vector3.Slerp(smoothedWorldMoveDirection, targetDirection, Mathf.Clamp01(8f * Time.deltaTime));
        }

        lastPosition = transform.position;
    }

    private void UpdateArrivalState()
    {
        var agent = Agent;
        if (!agent.isOnNavMesh || agent.pathPending)
        {
            hasReachedDestination = false;
            destinationArrivalTimer = 0f;
            return;
        }

        if (!agent.hasPath)
        {
            destinationArrivalTimer = 0f;
            return;
        }

        var arrivalDistanceReached = agent.remainingDistance <= Mathf.Max(Agent.stoppingDistance, destinationReachedThreshold);
        var planarSpeed = GetPlanarVelocity().magnitude;
        var isSettled = planarSpeed <= destinationReachedVelocityThreshold;

        if (arrivalDistanceReached && isSettled)
        {
            destinationArrivalTimer += Time.deltaTime;
            if (destinationArrivalTimer >= destinationReachedStableTime)
                Stop();
            return;
        }

        hasReachedDestination = false;
        destinationArrivalTimer = 0f;
    }

    private Vector3 GetPlanarVelocity()
    {
        var frameVelocity = GetFrameVelocity();
        if (frameVelocity.sqrMagnitude > DirectionDeadzone)
            return frameVelocity;

        var agentVelocity = Agent.velocity;
        agentVelocity.y = 0f;
        if (agentVelocity.sqrMagnitude > DirectionDeadzone)
            return agentVelocity;

        if (Body)
        {
            var rigidbodyVelocity = Body.linearVelocity;
            rigidbodyVelocity.y = 0f;
            if (rigidbodyVelocity.sqrMagnitude > DirectionDeadzone)
                return rigidbodyVelocity;
        }

        return Vector3.zero;
    }

    private Vector3 GetFrameVelocity()
    {
        if (Time.deltaTime <= 0f)
            return Vector3.zero;

        var frameVelocity = (transform.position - lastPosition) / Time.deltaTime;
        frameVelocity.y = 0f;
        return frameVelocity;
    }

    private Vector2 GetLocalMoveDirection(Vector3 worldMoveDirection)
    {
        if (worldMoveDirection.sqrMagnitude <= DirectionDeadzone)
            return Vector2.zero;

        var forward = GetConfiguredPlanarDirection(forwardDirection);
        var right = GetConfiguredPlanarDirection(rightDirection);
        if (forward.sqrMagnitude <= DirectionDeadzone || right.sqrMagnitude <= DirectionDeadzone)
            return Vector2.zero;

        var speedReference = Agent.speed > DirectionDeadzone ? Agent.speed : worldMoveDirection.magnitude;
        if (speedReference <= DirectionDeadzone)
            return Vector2.zero;

        return new Vector2(
            Mathf.Clamp(Vector3.Dot(worldMoveDirection, right) / speedReference, -1f, 1f),
            Mathf.Clamp(Vector3.Dot(worldMoveDirection, forward) / speedReference, -1f, 1f));
    }

    private Vector3 GetConfiguredPlanarDirection(LocalBasisDirection direction)
    {
        var configuredDirection = DirectionReference.TransformDirection(GetLocalBasisVector(direction));
        configuredDirection.y = 0f;
        return configuredDirection.sqrMagnitude > DirectionDeadzone ? configuredDirection.normalized : Vector3.zero;
    }

    private static Vector3 GetLocalBasisVector(LocalBasisDirection direction)
    {
        return direction switch
        {
            LocalBasisDirection.Forward => Vector3.forward,
            LocalBasisDirection.Backward => Vector3.back,
            LocalBasisDirection.Right => Vector3.right,
            LocalBasisDirection.Left => Vector3.left,
            _ => Vector3.forward
        };
    }
}
