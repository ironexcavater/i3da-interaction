#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(ConditionalFieldAttribute))]
public class ConditionalFieldDrawer : PropertyDrawer
{
    private const float HeaderTopPadding = 6f;
    private static readonly float HeaderHeight = HeaderTopPadding + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!SafeShouldShow(property)) return -EditorGUIUtility.standardVerticalSpacing;

        var height = EditorGUI.GetPropertyHeight(property, label, true);
        var data = (ConditionalFieldAttribute)attribute;
        if (string.IsNullOrWhiteSpace(data.header)) return height;
        return HeaderHeight + height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (!SafeShouldShow(property)) return;

        var data = (ConditionalFieldAttribute)attribute;
        var propertyLabel = new GUIContent(property.displayName, label.tooltip);

        if (!string.IsNullOrWhiteSpace(data.header))
        {
            var headerRect = new Rect(position.x, position.y + HeaderTopPadding, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(headerRect, data.header, EditorStyles.boldLabel);
            position = new Rect(position.x, position.y + HeaderHeight, position.width, position.height - HeaderHeight);
        }

        EditorGUI.PropertyField(position, property, propertyLabel, true);
    }

    private bool SafeShouldShow(SerializedProperty property)
    {
        if (property == null) return false;

        try
        {
            return ShouldShow(property);
        }
        catch
        {
            return false;
        }
    }

    private bool ShouldShow(SerializedProperty property)
    {
        var data = (ConditionalFieldAttribute)attribute;
        var condition = FindSiblingProperty(property, data.fieldName);
        if (condition == null) return false;

        if (!data.useEnum)
        {
            if (condition.propertyType != SerializedPropertyType.Boolean) return false;
            return condition.boolValue == data.boolValue;
        }

        var matches = false;
        if (condition.propertyType == SerializedPropertyType.Enum) matches = condition.enumValueIndex == data.enumValue;
        else if (condition.propertyType == SerializedPropertyType.Integer) matches = condition.intValue == data.enumValue;
        else return false;

        return data.invertEnumMatch ? !matches : matches;
    }

    private static SerializedProperty FindSiblingProperty(SerializedProperty property, string fieldName)
    {
        var path = property.propertyPath;
        var split = path.LastIndexOf('.');
        if (split < 0) return property.serializedObject.FindProperty(fieldName);

        var parentPath = path.Substring(0, split + 1);
        return property.serializedObject.FindProperty(parentPath + fieldName);
    }
}
#endif
