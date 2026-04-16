using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Cinemachine/Procedural/Extensions/Compound Collider Confiner 3D")]
public class CompoundColliderConfiner3D : CinemachineExtension
{
    private const float InsideTolerance = 0.0001f;

    [SerializeField] private Collider[] boundingVolumes = Array.Empty<Collider>();
    [SerializeField, Min(0f)] private float slowingDistance;

    private sealed class VcamExtraState : VcamExtraStateBase
    {
        public Vector3 PreviousCameraPosition;
    }

    public bool IsValid => HasValidBoundingVolumes();

    public bool SetBoundingVolumes(Collider[] newBoundingVolumes)
    {
        var sanitizedVolumes = SanitizeVolumes(newBoundingVolumes);
        if (SameVolumes(boundingVolumes, sanitizedVolumes))
            return false;

        boundingVolumes = sanitizedVolumes;
        return true;
    }

    public override float GetMaxDampTime()
    {
        return slowingDistance * 0.2f;
    }

    public override void OnTargetObjectWarped(
        CinemachineVirtualCameraBase vcam,
        Transform target,
        Vector3 positionDelta)
    {
        var extra = GetExtraState<VcamExtraState>(vcam);
        if (extra.Vcam.Follow == target)
            extra.PreviousCameraPosition += positionDelta;
    }

    private void OnValidate()
    {
        slowingDistance = Mathf.Max(0f, slowingDistance);
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage,
        ref CameraState state,
        float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Body || !IsValid)
            return;

        var extra = GetExtraState<VcamExtraState>(vcam);
        var desiredPosition = state.GetCorrectedPosition();
        var confinedPosition = GetConfinedPosition(desiredPosition);

        // If slowingDistance is enabled, we do not wait until the camera crosses the
        // boundary and then hard-snap it back. We ease the last stretch of travel so
        // the camera naturally slows as it approaches the room edge.
        if (slowingDistance > Epsilon && deltaTime >= 0f && vcam.PreviousStateIsValid)
            confinedPosition = EaseIntoBoundary(extra.PreviousCameraPosition, confinedPosition);

        state.PositionCorrection += confinedPosition - desiredPosition;
        extra.PreviousCameraPosition = state.GetCorrectedPosition();
    }

    private Vector3 GetConfinedPosition(Vector3 position)
    {
        // ClosestPoint has the key property we need:
        // - if the point is inside a collider, ClosestPoint returns the input point
        // - if the point is outside, it returns the nearest legal point on the surface
        //
        // That lets us treat many colliders as one compound room:
        // stay where you are if you are inside any volume, otherwise move to the
        // closest legal point across all volumes.
        if (IsInsideAnyVolume(position))
            return position;

        return TryGetClosestPoint(position, out var closestPoint) ? closestPoint : position;
    }

    private Vector3 EaseIntoBoundary(Vector3 previousPosition, Vector3 confinedPosition)
    {
        var direction = confinedPosition - previousPosition;
        var distance = direction.magnitude;
        if (distance <= Epsilon)
            return confinedPosition;

        var normalizedDirection = direction / distance;
        var distanceFromEdge = GetDistanceFromEdge(previousPosition, normalizedDirection, slowingDistance);
        var interpolation = Mathf.Clamp01(distanceFromEdge / slowingDistance);

        return Vector3.Lerp(
            previousPosition,
            confinedPosition,
            interpolation * interpolation * interpolation + 0.05f);
    }

    private bool IsInsideAnyVolume(Vector3 position)
    {
        for (var i = 0; i < boundingVolumes.Length; i++)
        {
            var volume = boundingVolumes[i];
            if (!IsValidVolume(volume))
                continue;

            if ((volume.ClosestPoint(position) - position).sqrMagnitude <= InsideTolerance)
                return true;
        }

        return false;
    }

    private bool TryGetClosestPoint(Vector3 position, out Vector3 closestPoint)
    {
        closestPoint = position;
        var closestDistance = float.PositiveInfinity;
        var foundPoint = false;

        for (var i = 0; i < boundingVolumes.Length; i++)
        {
            var volume = boundingVolumes[i];
            if (!IsValidVolume(volume))
                continue;

            var candidate = volume.ClosestPoint(position);
            var distance = (candidate - position).sqrMagnitude;
            if (distance >= closestDistance)
                continue;

            closestDistance = distance;
            closestPoint = candidate;
            foundPoint = true;
        }

        return foundPoint;
    }

    private float GetDistanceFromEdge(Vector3 position, Vector3 direction, float maxDistance)
    {
        position += direction * maxDistance;
        return maxDistance - (GetConfinedPosition(position) - position).magnitude;
    }

    private bool HasValidBoundingVolumes()
    {
        for (var i = 0; i < boundingVolumes.Length; i++)
        {
            if (IsValidVolume(boundingVolumes[i]))
                return true;
        }

        return false;
    }

    private static bool IsValidVolume(Collider volume)
    {
        return volume && volume.enabled && volume.gameObject.activeInHierarchy;
    }

    private static Collider[] SanitizeVolumes(Collider[] source)
    {
        if (source == null || source.Length == 0)
            return Array.Empty<Collider>();

        var sanitized = new List<Collider>(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var collider = source[i];
            if (!collider || sanitized.Contains(collider))
                continue;

            sanitized.Add(collider);
        }

        return sanitized.Count == 0 ? Array.Empty<Collider>() : sanitized.ToArray();
    }

    private static bool SameVolumes(Collider[] left, Collider[] right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }
}
