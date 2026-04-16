using System;
using UnityEngine;

[Serializable]
public struct AngularSector
{
    [Tooltip("Center direction in degrees. 0 = reference forward. Clockwise positive.")]
    [Range(-180f, 180f)]
    public float centerAngle;

    [Tooltip("Total angular width in degrees.")]
    [Range(1f, 360f)]
    public float arcWidth;

    [Tooltip("Display color used by editor tooling.")]
    public Color color;

    public float HalfArc => arcWidth * 0.5f;

    public static AngularSector FullCircle => new AngularSector
    {
        centerAngle = 0f,
        arcWidth = 360f,
        color = new Color(0.25f, 0.75f, 1f, 0.30f)
    };

    public bool Contains(float angle)
    {
        return Mathf.Abs(Mathf.DeltaAngle(angle, centerAngle)) <= HalfArc;
    }

    public float ClosestAngle(float angle)
    {
        if (Contains(angle))
            return angle;

        var sign = Mathf.DeltaAngle(centerAngle, angle) >= 0f ? 1f : -1f;
        return centerAngle + sign * HalfArc;
    }

    public AngularSector Normalized()
    {
        centerAngle = Mathf.Repeat(centerAngle + 180f, 360f) - 180f;
        arcWidth = Mathf.Clamp(arcWidth, 1f, 360f);
        color.a = Mathf.Clamp01(color.a);
        return this;
    }
}
