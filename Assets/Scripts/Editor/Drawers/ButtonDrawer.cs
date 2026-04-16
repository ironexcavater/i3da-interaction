#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor(typeof(MonoBehaviour), true, isFallback = true)]
public class ButtonDrawer : Editor
{
    private static readonly Dictionary<Type, List<(MethodInfo method, ButtonAttribute attribute)>> MethodCache = new();

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        DrawButtonsForTargetType(target != null ? target.GetType() : null);
    }

    private void DrawButtonsForTargetType(Type inspectedType)
    {
        if (inspectedType == null) return;

        var methods = GetButtonMethods(inspectedType);
        if (methods.Count == 0) return;

        EditorGUILayout.Space(8f);
        for (var i = 0; i < methods.Count; i++)
        {
            var entry = methods[i];
            var button = entry.attribute;

            if (button.PlayModeOnly && !Application.isPlaying) continue;
            if (button.EditModeOnly && Application.isPlaying) continue;
            if (!ShouldShowButton(target, inspectedType, button)) continue;

            var label = string.IsNullOrWhiteSpace(button.Label)
                ? ObjectNames.NicifyVariableName(entry.method.Name)
                : button.Label;
            var height = Mathf.Max(18f, button.Height);
            if (!GUILayout.Button(label, GUILayout.Height(height))) continue;

            for (var t = 0; t < targets.Length; t++)
            {
                var obj = targets[t];
                if (obj == null) continue;
                Undo.RecordObject(obj, $"Invoke {entry.method.Name}");
                entry.method.Invoke(obj, null);
                EditorUtility.SetDirty(obj);
            }
        }
    }

    private static bool ShouldShowButton(object inspectedObject, Type inspectedType, ButtonAttribute button)
    {
        if (inspectedObject == null) return false;
        if (string.IsNullOrWhiteSpace(button.VisibleIf)) return true;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var memberName = button.VisibleIf;
        bool? result = null;

        var property = inspectedType.GetProperty(memberName, flags);
        if (property != null && property.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0)
            result = (bool)property.GetValue(inspectedObject);

        if (!result.HasValue)
        {
            var field = inspectedType.GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(bool))
                result = (bool)field.GetValue(inspectedObject);
        }

        if (!result.HasValue)
        {
            var method = inspectedType.GetMethod(memberName, flags, null, Type.EmptyTypes, null);
            if (method != null && method.ReturnType == typeof(bool))
                result = (bool)method.Invoke(inspectedObject, null);
        }

        if (!result.HasValue) return true;
        return button.InvertVisible ? !result.Value : result.Value;
    }

    private static List<(MethodInfo method, ButtonAttribute attribute)> GetButtonMethods(Type type)
    {
        if (MethodCache.TryGetValue(type, out var cached)) return cached;

        var list = new List<(MethodInfo method, ButtonAttribute attribute)>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var methods = type.GetMethods(flags);
        for (var i = 0; i < methods.Length; i++)
        {
            var method = methods[i];
            if (method == null) continue;
            if (method.GetParameters().Length != 0) continue;
            if (method.ReturnType != typeof(void)) continue;

            var attr = method.GetCustomAttribute<ButtonAttribute>(true);
            if (attr == null) continue;

            list.Add((method, attr));
        }

        MethodCache[type] = list;
        return list;
    }
}
#endif
