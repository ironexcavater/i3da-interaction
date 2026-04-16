#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(AngularSector))]
public class AngularSectorDrawer : PropertyDrawer
{
    private const float ColorWidth = 44f;
    private const float Spacing = 6f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 2f + 8f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var centerAngle = property.FindPropertyRelative("centerAngle");
        var arcWidth = property.FindPropertyRelative("arcWidth");
        var color = property.FindPropertyRelative("color");

        position.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.LabelField(position, label, EditorStyles.miniBoldLabel);

        var row = position;
        row.y += EditorGUIUtility.singleLineHeight + 4f;

        var colorRect = new Rect(row.x, row.y, ColorWidth, row.height);
        var remainingWidth = row.width - ColorWidth - Spacing;
        var fieldWidth = (remainingWidth - Spacing) * 0.5f;
        var centerRect = new Rect(colorRect.xMax + Spacing, row.y, fieldWidth, row.height);
        var widthRect = new Rect(centerRect.xMax + Spacing, row.y, fieldWidth, row.height);

        EditorGUI.PropertyField(colorRect, color, GUIContent.none);
        EditorGUI.PropertyField(centerRect, centerAngle, new GUIContent("Center"));
        EditorGUI.PropertyField(widthRect, arcWidth, new GUIContent("Width"));
    }
}
#endif
