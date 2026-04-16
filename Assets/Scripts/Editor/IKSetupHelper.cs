#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class IKSetupHelper
{
    [MenuItem("Tools/IK Setup/Wire IK References")]
    public static void WireIKReferences()
    {
        // Source of truth: NavMeshPointClickInput already has the correct action refs wired.
        // We borrow them rather than recreating them (avoids 64-bit fileID precision loss).
        var navInput = Object.FindFirstObjectByType<NavMeshPointClickInput>(FindObjectsInactive.Include);
        if (navInput == null) { Debug.LogError("[IKSetup] NavMeshPointClickInput not found!"); return; }

        var navSO          = new SerializedObject(navInput);
        var pointerActionRef = navSO.FindProperty("pointerPositionAction").objectReferenceValue;
        var clickActionRef   = navSO.FindProperty("clickAction").objectReferenceValue;
        Debug.Log($"[IKSetup] Borrowed — pointer={pointerActionRef}, click={clickActionRef}");

        // ---- PickupInputHandler ----
        var pih = Object.FindFirstObjectByType<PickupInputHandler>(FindObjectsInactive.Include);
        if (pih != null)
        {
            var so = new SerializedObject(pih);
            so.FindProperty("pickableLayer").intValue       = 128;   // 1<<7 = Pickable
            so.FindProperty("rayDistance").floatValue       = 500f;
            so.FindProperty("ignoreClicksOverUi").boolValue = true;

            if (pointerActionRef) so.FindProperty("pointerPositionAction").objectReferenceValue = pointerActionRef;
            if (clickActionRef)   so.FindProperty("clickAction").objectReferenceValue           = clickActionRef;

            var cam = Camera.main;
            if (cam) so.FindProperty("worldCamera").objectReferenceValue = cam;

            var characterCtrl = Object.FindFirstObjectByType<PlayerCharacterController>(FindObjectsInactive.Include);
            if (characterCtrl) so.FindProperty("characterController").objectReferenceValue = characterCtrl;

            so.ApplyModifiedProperties();
            Debug.Log("[IKSetup] PickupInputHandler wired OK.");
        }
        else Debug.LogWarning("[IKSetup] PickupInputHandler not found!");

        // ---- NavMeshPointClickInput — block clicks on Pickable layer ----
        // blockingLayers on NavMeshPointClickInput: when the front-most raycast hit is on a
        // blocking layer the script returns false before setting any destination.
        // This stops point-and-click navigation from firing when the user clicks a pickup item.
        navSO.FindProperty("blockingLayers").intValue = 128;  // 1<<7 = Pickable
        navSO.ApplyModifiedProperties();
        Debug.Log("[IKSetup] NavMeshPointClickInput blockingLayers set to Pickable (128).");

        // ---- CharacterAnimationController ----
        var animCtrl = Object.FindFirstObjectByType<CharacterAnimationController>(FindObjectsInactive.Include);
        if (animCtrl != null)
        {
            var so2 = new SerializedObject(animCtrl);

            var cam = Camera.main;
            if (cam) so2.FindProperty("worldCamera").objectReferenceValue = cam;

            if (pointerActionRef) so2.FindProperty("pointerPositionAction").objectReferenceValue = pointerActionRef;

            // All layers except Pickable (7), IgnoreRaycast (2), UI layers
            so2.FindProperty("lookAtRaycastLayers").intValue = unchecked((int)4294967067u);

            var cc = animCtrl.GetComponent<PlayerCharacterController>();
            if (cc) so2.FindProperty("characterController").objectReferenceValue = cc;

            so2.ApplyModifiedProperties();
            Debug.Log("[IKSetup] CharacterAnimationController wired OK.");
        }
        else Debug.LogWarning("[IKSetup] CharacterAnimationController not found!");

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[IKSetup] Done — use Ctrl+S to save.");
    }

    [MenuItem("Tools/IK Setup/Save Scene")]
    public static void SaveScene()
    {
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[IKSetup] Scene saved.");
    }
}
#endif
