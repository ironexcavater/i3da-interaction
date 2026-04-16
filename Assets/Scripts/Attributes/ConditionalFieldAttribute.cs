using UnityEngine;

public sealed class ConditionalFieldAttribute : PropertyAttribute
{
    public readonly string fieldName;
    public readonly bool useEnum;
    public readonly bool boolValue;
    public readonly int enumValue;
    public readonly bool invertEnumMatch;
    public readonly string header;

    public ConditionalFieldAttribute(string fieldName, bool showWhenTrue = true, string header = null)
    {
        this.fieldName = fieldName;
        useEnum = false;
        boolValue = showWhenTrue;
        enumValue = 0;
        invertEnumMatch = false;
        this.header = header;
    }

    public ConditionalFieldAttribute(string fieldName, int expectedEnumValue, string header = null)
        : this(fieldName, expectedEnumValue, false, header)
    {
    }

    public ConditionalFieldAttribute(string fieldName, int expectedEnumValue, bool invertEnumMatch, string header = null)
    {
        this.fieldName = fieldName;
        useEnum = true;
        boolValue = true;
        enumValue = expectedEnumValue;
        this.invertEnumMatch = invertEnumMatch;
        this.header = header;
    }
}
