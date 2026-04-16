using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PickableItem : MonoBehaviour
{
    [Header("Grab")]
    [SerializeField] private Transform grabPoint;
    [SerializeField, Min(0f)] private float pickupRadius = 1.2f;

    [Header("Approach")]
    [Tooltip("Optional pivot for where the character should stand relative to the item. Defaults to this transform.")]
    [SerializeField] private Transform approachPivot;

    [Tooltip("Character stops this many metres from the approach pivot before the pickup animation starts.")]
    [SerializeField, Min(0.1f)] private float approachDistance = 1.0f;

    [Tooltip("Allowed approach sectors around the grab point. Leave empty for a single full-circle sector.")]
    [SerializeField] private List<AngularSector> approachSectors = new List<AngularSector> { AngularSector.FullCircle };

    public Transform GrabPoint => grabPoint ? grabPoint : transform;
    public Transform ApproachPivot => approachPivot ? approachPivot : transform;
    public float PickupRadius => pickupRadius;
    public float ApproachDistance => approachDistance;
    public IReadOnlyList<AngularSector> ApproachSectors => approachSectors;
    public bool IsPickedUp { get; private set; }

    public event Action<PickableItem> OnCollected;

    public int GetApproachSectorCount()
    {
        EnsureApproachSectors();
        return approachSectors.Count;
    }

    public AngularSector GetApproachSector(int index)
    {
        EnsureApproachSectors();
        return approachSectors[index];
    }

    public void SetApproachSector(int index, AngularSector sector)
    {
        EnsureApproachSectors();
        approachSectors[index] = sector.Normalized();
    }

    public void AddApproachSector(AngularSector sector)
    {
        EnsureApproachSectors();
        approachSectors.Add(sector.Normalized());
    }

    public void ResetApproachSectorsToFullCircle()
    {
        if (approachSectors == null)
            approachSectors = new List<AngularSector>();

        approachSectors.Clear();
        approachSectors.Add(AngularSector.FullCircle);
    }

    public IEnumerable<Vector3> GetApproachPositions(Vector3 fromPosition, int samplesPerSector = 5)
    {
        if (!TryGetApproachBasis(fromPosition, out var origin, out var grabForward, out var preferredAngle, out var selectedSector))
            yield break;

        var sampleCount = Mathf.Max(1, samplesPerSector);
        var yieldedAngles = new HashSet<int>();

        yield return CreateApproachPosition(origin, grabForward, preferredAngle);
        yieldedAngles.Add(GetAngleKey(preferredAngle));

        if (sampleCount == 1)
            yield break;

        if (selectedSector.arcWidth >= 359.9f)
        {
            var stepAngle = 360f / sampleCount;
            for (var step = 1; yieldedAngles.Count < sampleCount; step++)
            {
                var offset = stepAngle * step;
                var positiveAngle = preferredAngle + offset;
                if (yieldedAngles.Add(GetAngleKey(positiveAngle)))
                    yield return CreateApproachPosition(origin, grabForward, positiveAngle);
                if (yieldedAngles.Count >= sampleCount)
                    yield break;

                var negativeAngle = preferredAngle - offset;
                if (yieldedAngles.Add(GetAngleKey(negativeAngle)))
                    yield return CreateApproachPosition(origin, grabForward, negativeAngle);
            }

            yield break;
        }

        var minAngle = selectedSector.centerAngle - selectedSector.HalfArc;
        var maxAngle = selectedSector.centerAngle + selectedSector.HalfArc;
        var stepSize = selectedSector.arcWidth / Mathf.Max(1, sampleCount - 1);
        for (var step = 1; yieldedAngles.Count < sampleCount; step++)
        {
            var offset = stepSize * step;

            var positiveAngle = preferredAngle + offset;
            if (positiveAngle <= maxAngle && yieldedAngles.Add(GetAngleKey(positiveAngle)))
                yield return CreateApproachPosition(origin, grabForward, positiveAngle);
            if (yieldedAngles.Count >= sampleCount)
                yield break;

            var negativeAngle = preferredAngle - offset;
            if (negativeAngle >= minAngle && yieldedAngles.Add(GetAngleKey(negativeAngle)))
                yield return CreateApproachPosition(origin, grabForward, negativeAngle);

            if (positiveAngle > maxAngle && negativeAngle < minAngle)
                yield break;
        }
    }

    public Vector3 GetApproachPosition(Vector3 fromPosition)
    {
        if (!TryGetApproachBasis(fromPosition, out var origin, out var grabForward, out var preferredAngle, out _))
            return ApproachPivot.position + GetGrabForward() * approachDistance;

        return CreateApproachPosition(origin, grabForward, preferredAngle);
    }

    public void Collect()
    {
        if (IsPickedUp)
            return;

        IsPickedUp = true;
        OnCollected?.Invoke(this);

        foreach (var rendererComponent in GetComponentsInChildren<Renderer>())
            rendererComponent.enabled = false;

        foreach (var colliderComponent in GetComponentsInChildren<Collider>())
            colliderComponent.enabled = false;
    }

    private void OnValidate()
    {
        approachDistance = Mathf.Max(0.1f, approachDistance);
        pickupRadius = Mathf.Max(0f, pickupRadius);
        EnsureApproachSectors();

        for (var i = 0; i < approachSectors.Count; i++)
            approachSectors[i] = approachSectors[i].Normalized();
    }

    private void EnsureApproachSectors()
    {
        if (approachSectors == null)
            approachSectors = new List<AngularSector>();

        if (approachSectors.Count == 0)
            approachSectors.Add(AngularSector.FullCircle);
    }

    private bool TryGetApproachBasis(
        Vector3 fromPosition,
        out Vector3 origin,
        out Vector3 grabForward,
        out float preferredAngle,
        out AngularSector selectedSector)
    {
        origin = ApproachPivot.position;
        grabForward = GetGrabForward();

        var desiredDirection = fromPosition - origin;
        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude <= 0.001f)
            desiredDirection = grabForward;
        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude <= 0.001f)
        {
            preferredAngle = 0f;
            selectedSector = AngularSector.FullCircle;
            return false;
        }

        desiredDirection.Normalize();
        var desiredAngle = Vector3.SignedAngle(grabForward, desiredDirection, Vector3.up);

        EnsureApproachSectors();

        selectedSector = AngularSector.FullCircle;
        preferredAngle = desiredAngle;
        var smallestDelta = float.PositiveInfinity;
        for (var i = 0; i < approachSectors.Count; i++)
        {
            var sector = approachSectors[i].Normalized();
            var candidateAngle = sector.arcWidth >= 359.9f
                ? desiredAngle
                : sector.ClosestAngle(desiredAngle);
            var delta = Mathf.Abs(Mathf.DeltaAngle(desiredAngle, candidateAngle));
            if (delta >= smallestDelta)
                continue;

            smallestDelta = delta;
            preferredAngle = candidateAngle;
            selectedSector = sector;
        }

        return true;
    }

    private Vector3 GetGrabForward()
    {
        var grabForward = GrabPoint.forward;
        grabForward.y = 0f;
        if (grabForward.sqrMagnitude < 0.001f)
            grabForward = Vector3.forward;
        grabForward.Normalize();
        return grabForward;
    }

    private Vector3 CreateApproachPosition(Vector3 origin, Vector3 grabForward, float angle)
    {
        var approachDirection = Quaternion.AngleAxis(angle, Vector3.up) * grabForward;
        return origin + approachDirection * approachDistance;
    }

    private static int GetAngleKey(float angle)
    {
        return Mathf.RoundToInt(Mathf.Repeat(angle + 180f, 360f) * 10f);
    }

    private void OnDrawGizmosSelected()
    {
        var pivot = GrabPoint;
        var approach = ApproachPivot;

        Gizmos.color = new Color(0f, 1f, 0.3f, 0.85f);
        Gizmos.DrawWireSphere(pivot.position, 0.06f);
        Gizmos.DrawLine(transform.position, pivot.position);

        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.85f);
        Gizmos.DrawWireSphere(approach.position, 0.05f);
        Gizmos.DrawLine(approach.position, approach.position + GetGrabForward() * approachDistance);

        Gizmos.color = new Color(1f, 0.8f, 0f, 0.3f);
        Gizmos.DrawWireSphere(approach.position, pickupRadius);
    }
}
