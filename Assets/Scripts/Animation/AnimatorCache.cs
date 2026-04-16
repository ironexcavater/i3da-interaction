using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class AnimatorCache : MonoBehaviour
{
    private Dictionary<string, int> parameterHashes;

    private Animator animator;
    private Animator Animator => animator ? animator : animator = GetComponent<Animator>();

    private void Awake() => Initialize();

    private void OnValidate()
    {
        animator = GetComponent<Animator>();
        parameterHashes = null;
    }

    public bool TryGetHash(string parameterName, out int hash)
    {
        Initialize();
        return parameterHashes.TryGetValue(parameterName, out hash);
    }

    public int GetHash(string parameterName)
    {
        Initialize();
        return parameterHashes.TryGetValue(parameterName, out var hash)
            ? hash
            : throw new KeyNotFoundException($"Animator parameter '{parameterName}' was not found.");
    }

    public bool HasParameter(string parameterName)
    {
        Initialize();
        return parameterHashes.ContainsKey(parameterName);
    }

    public bool TrySet<T>(string parameterName, T value)
    {
        if (!TryGetHash(parameterName, out var hash)) return false;

        switch (value)
        {
            case float floatValue:
                Animator.SetFloat(hash, floatValue);
                return true;
            case bool boolValue:
                Animator.SetBool(hash, boolValue);
                return true;
            case int intValue:
                Animator.SetInteger(hash, intValue);
                return true;
            default:
                return false;
        }
    }

    public bool TrySetTrigger(string parameterName)
    {
        if (!TryGetHash(parameterName, out var hash)) return false;
        Animator.SetTrigger(hash);
        return true;
    }

    public bool TryResetTrigger(string parameterName)
    {
        if (!TryGetHash(parameterName, out var hash)) return false;
        Animator.ResetTrigger(hash);
        return true;
    }

    public bool TryGet<T>(string parameterName, out T value)
    {
        if (!TryGetHash(parameterName, out var hash))
        {
            value = default;
            return false;
        }

        var result = typeof(T) switch
        {
            var type when type == typeof(float) => (object)Animator.GetFloat(hash),
            var type when type == typeof(bool) => Animator.GetBool(hash),
            var type when type == typeof(int) => Animator.GetInteger(hash),
            _ => null
        };

        if (result == null)
        {
            value = default;
            return false;
        }

        value = (T)result;
        return true;
    }

    private void Initialize()
    {
        if (parameterHashes != null) return;

        parameterHashes = new Dictionary<string, int>();
        foreach (var parameter in Animator.parameters)
            parameterHashes[parameter.name] = parameter.nameHash;
    }
}
