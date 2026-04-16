using UnityEngine.InputSystem;

public static class InputActionReferenceExtensions
{
    public static void SetEnabled(this InputActionReference actionReference, bool enabled)
    {
        if (enabled) actionReference?.action?.Enable();
        else actionReference?.action?.Disable();
    }
}
