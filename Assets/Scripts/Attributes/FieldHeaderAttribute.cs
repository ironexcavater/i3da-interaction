using UnityEngine;

public sealed class FieldHeaderAttribute : PropertyAttribute
{
    public readonly string title;
    public readonly string conditionField;
    public readonly bool useEnumCondition;
    public readonly int expectedEnumValue;

    public FieldHeaderAttribute(string title = null)
    {
        this.title = title;
        conditionField = null;
        useEnumCondition = false;
        expectedEnumValue = 0;
    }

    public FieldHeaderAttribute(string title, string conditionField, int expectedEnumValue)
    {
        this.title = title;
        this.conditionField = conditionField;
        useEnumCondition = true;
        this.expectedEnumValue = expectedEnumValue;
    }
}
