using System;
using UnityEngine;
using UnityEngine.InputSystem;

public static class PointerWorldUtility
{
    public static bool TryGetPointerRay(Camera worldCamera, InputActionReference pointerPositionAction, out Ray ray, out Vector2 screenPosition)
    {
        ray = default;
        screenPosition = default;

        if (!worldCamera)
            return false;

        screenPosition = ReadPointerScreenPosition(pointerPositionAction);
        ray = worldCamera.ScreenPointToRay(screenPosition);
        return true;
    }

    public static Vector2 ReadPointerScreenPosition(InputActionReference pointerPositionAction)
    {
        if (pointerPositionAction != null && pointerPositionAction.action != null)
            return pointerPositionAction.action.ReadValue<Vector2>();

        return new Vector2(Input.mousePosition.x, Input.mousePosition.y);
    }

    public static RaycastHit[] GetSortedHits(Ray ray, float rayDistance, LayerMask layerMask, QueryTriggerInteraction triggerInteraction)
    {
        var hits = Physics.RaycastAll(ray, rayDistance, layerMask, triggerInteraction);
        if (hits == null || hits.Length <= 1)
            return hits;

        Array.Sort(hits, static (left, right) => left.distance.CompareTo(right.distance));
        return hits;
    }
}
