#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(PickableItem))]
[CanEditMultipleObjects]
public class PickableItemEditor : Editor
{
    private SerializedProperty grabPointProperty;
    private SerializedProperty approachPivotProperty;
    private SerializedProperty pickupRadiusProperty;
    private SerializedProperty approachDistanceProperty;
    private SerializedProperty approachSectorsProperty;
    private ReorderableList approachSectorList;

    private const float DialSize = 156f;
    private const float DialPadding = 10f;

    private void OnEnable()
    {
        grabPointProperty = serializedObject.FindProperty("grabPoint");
        approachPivotProperty = serializedObject.FindProperty("approachPivot");
        pickupRadiusProperty = serializedObject.FindProperty("pickupRadius");
        approachDistanceProperty = serializedObject.FindProperty("approachDistance");
        approachSectorsProperty = serializedObject.FindProperty("approachSectors");

        approachSectorList = new ReorderableList(serializedObject, approachSectorsProperty, true, true, true, true);
        approachSectorList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Allowed Approach Sectors");
        approachSectorList.drawElementCallback = DrawApproachSectorElement;
        approachSectorList.elementHeightCallback = index =>
        {
            var element = approachSectorsProperty.GetArrayElementAtIndex(index);
            return EditorGUI.GetPropertyHeight(element, true) + 8f;
        };
        approachSectorList.onAddCallback = list =>
        {
            var insertIndex = list.serializedProperty.arraySize;
            list.serializedProperty.InsertArrayElementAtIndex(insertIndex);
            var element = list.serializedProperty.GetArrayElementAtIndex(insertIndex);
            element.FindPropertyRelative("centerAngle").floatValue = 0f;
            element.FindPropertyRelative("arcWidth").floatValue = 90f;
            element.FindPropertyRelative("color").colorValue = new Color(0.29f, 0.76f, 1f, 0.32f);
            serializedObject.ApplyModifiedProperties();
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(grabPointProperty);
        EditorGUILayout.PropertyField(approachPivotProperty);
        EditorGUILayout.PropertyField(pickupRadiusProperty);

        EditorGUILayout.Space(8f);
        DrawApproachSection();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawApproachSection()
    {
        EditorGUILayout.LabelField("Approach", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(approachDistanceProperty);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawInspectorDial();
            EditorGUILayout.Space(6f);
            DrawSectorToolbar();
            EditorGUILayout.Space(4f);
            approachSectorList.DoLayoutList();
        }
    }

    private void DrawSectorToolbar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add 90° Sector"))
            {
                approachSectorsProperty.arraySize += 1;
                var element = approachSectorsProperty.GetArrayElementAtIndex(approachSectorsProperty.arraySize - 1);
                element.FindPropertyRelative("centerAngle").floatValue = 0f;
                element.FindPropertyRelative("arcWidth").floatValue = 90f;
                element.FindPropertyRelative("color").colorValue = new Color(0.29f, 0.76f, 1f, 0.32f);
            }

            if (GUILayout.Button("Reset To Full Circle"))
            {
                approachSectorsProperty.arraySize = 1;
                var element = approachSectorsProperty.GetArrayElementAtIndex(0);
                element.FindPropertyRelative("centerAngle").floatValue = AngularSector.FullCircle.centerAngle;
                element.FindPropertyRelative("arcWidth").floatValue = AngularSector.FullCircle.arcWidth;
                element.FindPropertyRelative("color").colorValue = AngularSector.FullCircle.color;
            }
        }
    }

    private void DrawApproachSectorElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        var element = approachSectorsProperty.GetArrayElementAtIndex(index);
        rect.y += 4f;
        rect.height = EditorGUI.GetPropertyHeight(element, true);
        EditorGUI.PropertyField(rect, element, new GUIContent($"Sector {index + 1}"), true);
    }

    private void DrawInspectorDial()
    {
        var radius = DialSize * 0.5f - DialPadding;
        var dialRect = GUILayoutUtility.GetRect(DialSize, DialSize + 18f, GUILayout.ExpandWidth(true));
        var discRect = new Rect(dialRect.x + (dialRect.width - DialSize) * 0.5f, dialRect.y, DialSize, DialSize);
        var center = discRect.center;

        Handles.BeginGUI();
        DrawFilledDisc2D(center, radius, new Color(0.11f, 0.12f, 0.14f, 1f));
        DrawCircle2D(center, radius, new Color(0.52f, 0.58f, 0.64f, 1f), 72);
        DrawCrosshair2D(center, radius);

        for (var i = 0; i < approachSectorsProperty.arraySize; i++)
        {
            var sector = approachSectorsProperty.GetArrayElementAtIndex(i);
            DrawDialSectorFilled(
                center,
                radius,
                sector.FindPropertyRelative("centerAngle").floatValue,
                sector.FindPropertyRelative("arcWidth").floatValue,
                sector.FindPropertyRelative("color").colorValue);
        }

        Handles.color = Color.white;
        Handles.DrawSolidDisc(new Vector3(center.x, center.y), Vector3.forward, 2.5f);
        Handles.EndGUI();

        var labelRect = new Rect(dialRect.x, discRect.yMax + 2f, dialRect.width, 16f);
        EditorGUI.LabelField(labelRect, "Top = grab point forward", EditorStyles.centeredGreyMiniLabel);
    }

    private void OnSceneGUI()
    {
        var item = (PickableItem)target;
        if (item == null)
            return;

        var pivot = item.GrabPoint;
        var origin = item.ApproachPivot.position;
        var grabForward = pivot.forward;
        grabForward.y = 0f;
        if (grabForward.sqrMagnitude < 0.001f)
            grabForward = Vector3.forward;
        grabForward.Normalize();

        var radius = item.ApproachDistance;

        Handles.color = new Color(0.95f, 0.85f, 0.22f, 0.55f);
        Handles.DrawWireDisc(origin, Vector3.up, radius);

        for (var i = 0; i < item.GetApproachSectorCount(); i++)
            DrawSectorHandles(item, i, origin, grabForward, radius);
    }

    private void DrawSectorHandles(PickableItem item, int index, Vector3 origin, Vector3 grabForward, float radius)
    {
        var sector = item.GetApproachSector(index);
        var fromDir = Quaternion.AngleAxis(sector.centerAngle - sector.HalfArc, Vector3.up) * grabForward;

        Handles.color = sector.color;
        Handles.DrawSolidArc(origin, Vector3.up, fromDir, sector.arcWidth, radius);

        var outlineColor = new Color(sector.color.r, sector.color.g, sector.color.b, 1f);
        Handles.color = outlineColor;
        Handles.DrawWireArc(origin, Vector3.up, fromDir, sector.arcWidth, radius);

        var centerDir = Quaternion.AngleAxis(sector.centerAngle, Vector3.up) * grabForward;
        Handles.color = Color.Lerp(outlineColor, Color.white, 0.35f);
        Handles.DrawLine(origin, origin + centerDir * radius);

        var centerKnobPos = origin + centerDir * radius;
        var centerKnobSize = HandleUtility.GetHandleSize(centerKnobPos) * 0.12f;

        EditorGUI.BeginChangeCheck();
        var movedCenter = Handles.FreeMoveHandle(centerKnobPos, centerKnobSize, Vector3.zero, Handles.SphereHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            var direction = movedCenter - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                sector.centerAngle = Mathf.Round(Vector3.SignedAngle(grabForward, direction.normalized, Vector3.up));
                ApplySectorChange(item, index, sector, "Rotate Approach Sector");
            }
        }

        DrawEdgeKnob(item, index, origin, grabForward, radius, sector, -1f);
        DrawEdgeKnob(item, index, origin, grabForward, radius, sector, 1f);
    }

    private void DrawEdgeKnob(PickableItem item, int index, Vector3 origin, Vector3 grabForward, float radius, AngularSector sector, float sign)
    {
        var edgeAngle = sector.centerAngle + sign * sector.HalfArc;
        var edgeDirection = Quaternion.AngleAxis(edgeAngle, Vector3.up) * grabForward;
        var edgePosition = origin + edgeDirection * radius;
        var knobSize = HandleUtility.GetHandleSize(edgePosition) * 0.09f;

        Handles.color = Color.Lerp(sector.color, Color.white, 0.45f);

        EditorGUI.BeginChangeCheck();
        var movedEdge = Handles.FreeMoveHandle(edgePosition, knobSize, Vector3.zero, Handles.SphereHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            var direction = movedEdge - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                var newEdgeAngle = Vector3.SignedAngle(grabForward, direction.normalized, Vector3.up);
                sector.arcWidth = Mathf.Clamp(Mathf.Abs(Mathf.DeltaAngle(sector.centerAngle, newEdgeAngle)) * 2f, 1f, 360f);
                ApplySectorChange(item, index, sector, "Resize Approach Sector");
            }
        }
    }

    private static void ApplySectorChange(PickableItem item, int index, AngularSector sector, string undoLabel)
    {
        Undo.RecordObject(item, undoLabel);
        item.SetApproachSector(index, sector);
        EditorUtility.SetDirty(item);
    }

    private static Vector2 AngleToGui(float degrees)
    {
        var radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians));
    }

    private static void DrawDialSectorFilled(Vector2 center, float radius, float centerAngle, float arcWidth, Color color)
    {
        var segments = Mathf.Max(3, Mathf.RoundToInt(Mathf.Abs(arcWidth) / 4f));
        var start = centerAngle - arcWidth * 0.5f;

        Handles.color = color;
        for (var i = 0; i < segments; i++)
        {
            var a0 = start + arcWidth * i / segments;
            var a1 = start + arcWidth * (i + 1) / segments;
            var p0 = center + AngleToGui(a0) * radius;
            var p1 = center + AngleToGui(a1) * radius;
            Handles.DrawAAConvexPolygon(new Vector3(center.x, center.y), new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
        }
    }

    private static void DrawCircle2D(Vector2 center, float radius, Color color, int segments)
    {
        Handles.color = color;
        for (var i = 0; i < segments; i++)
        {
            var a0 = center + AngleToGui(i * 360f / segments) * radius;
            var a1 = center + AngleToGui((i + 1) * 360f / segments) * radius;
            Handles.DrawLine(new Vector3(a0.x, a0.y), new Vector3(a1.x, a1.y));
        }
    }

    private static void DrawFilledDisc2D(Vector2 center, float radius, Color color)
    {
        const int segments = 48;
        var points = new Vector3[segments];

        for (var i = 0; i < segments; i++)
        {
            var point = center + AngleToGui(i * 360f / segments) * radius;
            points[i] = new Vector3(point.x, point.y);
        }

        Handles.color = color;
        Handles.DrawAAConvexPolygon(points);
    }

    private static void DrawCrosshair2D(Vector2 center, float radius)
    {
        Handles.color = new Color(1f, 1f, 1f, 0.18f);
        Handles.DrawLine(new Vector3(center.x, center.y - radius), new Vector3(center.x, center.y + radius));
        Handles.DrawLine(new Vector3(center.x - radius, center.y), new Vector3(center.x + radius, center.y));

        Handles.color = new Color(1f, 1f, 1f, 0.55f);
        var topOuter = center + AngleToGui(0f) * radius;
        var topInner = center + AngleToGui(0f) * (radius - 10f);
        Handles.DrawLine(new Vector3(topOuter.x, topOuter.y), new Vector3(topInner.x, topInner.y));
    }
}
#endif
