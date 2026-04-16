using Unity.Cinemachine;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Camera/Room Camera Controller")]
[RequireComponent(typeof(CinemachineCamera))]
public class RoomCameraController : MonoBehaviour
{
    private static RoomCameraController activeRoomCamera;

    public enum CameraPositioningMode
    {
        Static = 0,
        FollowTarget = 1,
        SplineDolly = 2
    }

    public enum CameraFramingMode
    {
        None = 0,
        LookAtTarget = 1
    }

    public enum CameraConfinementMode
    {
        None = 0,
        ConfineToRoomZone = 1
    }

    public enum CameraOcclusionMode
    {
        None = 0,
        DeoccludeTarget = 1
    }

    [FieldHeader("Room Zone")]
    [SerializeField] private RoomCameraZone roomZone;

    [FieldHeader("Activation")]
    [SerializeField] private bool active;
    [SerializeField] private bool activeOnStart;
    [SerializeField] private int inactivePriority;
    [SerializeField] private int activePriority = 100;

    [FieldHeader("Camera Behavior")]
    [SerializeField] private CameraPositioningMode positioning = CameraPositioningMode.FollowTarget;
    [SerializeField] private CameraFramingMode framing = CameraFramingMode.LookAtTarget;
    [ConditionalField(nameof(positioning), (int)CameraPositioningMode.FollowTarget)]
    [SerializeField] private CameraConfinementMode confinement;
    [ConditionalField(nameof(positioning), (int)CameraPositioningMode.Static, true)]
    [SerializeField] private CameraOcclusionMode occlusion;

    private CinemachineCamera cinemachineCamera;
    private CinemachineFollow follow;
    private CinemachineRotationComposer rotationComposer;
    private CinemachineSplineDolly splineDolly;
    private CompoundColliderConfiner3D compoundConfiner;
    private CinemachineDeoccluder deoccluder;
    private bool runtimeInitialized;
    private bool configurationDirty;
    private bool activationDirty;
#if UNITY_EDITOR
    private bool editorRefreshQueued;
#endif

    public RoomCameraZone RoomZone => roomZone ? roomZone : roomZone = FindRoomZone();
    public bool Active => active;

    private CinemachineCamera VirtualCamera => cinemachineCamera ? cinemachineCamera : cinemachineCamera = GetComponent<CinemachineCamera>();
    private Transform TrackingTarget => VirtualCamera.Target.TrackingTarget;
    private Transform LookTarget
    {
        get
        {
            var target = VirtualCamera.Target;
            return target.CustomLookAtTarget && target.LookAtTarget != null
                ? target.LookAtTarget
                : target.TrackingTarget;
        }
    }

    private bool HasLinkedRoomZone => RoomZone != null;

    public void SetRoomActive(bool value)
    {
        SetActive(value);
    }

    public void ActivateRoom()
    {
        SetActive(true);
    }

    public void DeactivateRoom()
    {
        SetActive(false);
    }

    public void SetActive(bool value)
    {
        runtimeInitialized = true;
        active = value;

        if (Application.isPlaying)
        {
            SyncActivationState();
            return;
        }

#if UNITY_EDITOR
        QueueEditorRefresh();
#endif
    }

    public void SetTargets(Transform trackingTarget, Transform lookAtTarget = null)
    {
        var target = VirtualCamera.Target;
        target.TrackingTarget = trackingTarget;
        target.LookAtTarget = lookAtTarget;
        target.CustomLookAtTarget = lookAtTarget != null && lookAtTarget != trackingTarget;
        VirtualCamera.Target = target;
        RequestConfigurationRefresh();
    }

    public void SetRoomZone(RoomCameraZone zone, bool syncZone = true)
    {
        roomZone = zone;
        if (syncZone && zone != null)
            zone.AssignRoomCamera(this);

        RequestConfigurationRefresh();
    }

    [Button("Sync Room Camera", editModeOnly: true)]
    public void RefreshConfiguration()
    {
        ClampSettings();
        EnsureZoneLink();
        SyncManagedComponents();
        SyncConfinerVolumes();
        SyncActivationState();
        ClearDirtyFlags();
    }

    private void Reset()
    {
        CacheReferences();
        roomZone = FindRoomZone();
        active = activeOnStart;
#if UNITY_EDITOR
        QueueEditorRefresh();
#endif
    }

    private void OnEnable()
    {
        CacheReferences();

        if (Application.isPlaying)
        {
            InitializeRuntimeState();
            MarkDirty();
            RefreshRuntimeBindings();
        }
#if UNITY_EDITOR
        else
        {
            QueueEditorRefresh();
        }
#endif
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
            return;
        if (!configurationDirty && !activationDirty)
            return;

        RefreshRuntimeBindings();
    }

    private void OnDisable()
    {
        if (activeRoomCamera == this)
            activeRoomCamera = null;
    }

    private void OnValidate()
    {
        CacheReferences();
        ClampSettings();

        if (Application.isPlaying)
        {
            runtimeInitialized = true;
            MarkDirty();
            return;
        }

#if UNITY_EDITOR
        QueueEditorRefresh();
#endif
    }

    private void CacheReferences()
    {
        cinemachineCamera = GetComponent<CinemachineCamera>();
        follow = GetComponent<CinemachineFollow>();
        rotationComposer = GetComponent<CinemachineRotationComposer>();
        splineDolly = GetComponent<CinemachineSplineDolly>();
        compoundConfiner = GetComponent<CompoundColliderConfiner3D>();
        deoccluder = GetComponent<CinemachineDeoccluder>();
    }

    private void InitializeRuntimeState()
    {
        if (runtimeInitialized)
            return;

        active = activeOnStart;
        runtimeInitialized = true;
    }

    private void RefreshRuntimeBindings()
    {
        ClampSettings();

        if (configurationDirty)
        {
            EnsureZoneLink();
            SyncManagedComponents();
            SyncConfinerVolumes();
            configurationDirty = false;
        }

        if (activationDirty)
            SyncActivationState();
    }

    private void MarkDirty()
    {
        configurationDirty = true;
        activationDirty = true;
    }

    private void ClearDirtyFlags()
    {
        configurationDirty = false;
        activationDirty = false;
    }

    private void ClampSettings()
    {
        activePriority = Mathf.Max(activePriority, inactivePriority);

        var target = VirtualCamera.Target;
        if (target.CustomLookAtTarget && target.LookAtTarget == null)
            target.CustomLookAtTarget = false;

        VirtualCamera.Target = target;
    }

    private void EnsureZoneLink()
    {
        if (RoomZone)
            RoomZone.AssignRoomCamera(this);
    }

    private void SyncActivationState()
    {
        if (Application.isPlaying)
        {
            if (active)
                ClaimActiveRoomCamera();
            else if (activeRoomCamera == this)
                activeRoomCamera = null;
        }

        ApplyPriority();
        activationDirty = false;
    }

    private void ApplyPriority()
    {
        VirtualCamera.Priority = active ? activePriority : inactivePriority;
        if (active)
            VirtualCamera.Prioritize();
    }

    private void ClaimActiveRoomCamera()
    {
        if (activeRoomCamera == this)
            return;

        var previousRoomCamera = activeRoomCamera;
        activeRoomCamera = this;

        if (previousRoomCamera != null)
            previousRoomCamera.SetActiveWithoutClaim(false);
    }

    private void SetActiveWithoutClaim(bool value)
    {
        active = value;
        ApplyPriority();
        activationDirty = false;
    }

    private void SyncManagedComponents()
    {
        // The controller owns which Cinemachine building blocks should exist.
        // Tuning still lives on the real Cinemachine components themselves.
        SyncComponent(ref follow, positioning == CameraPositioningMode.FollowTarget);
        SyncComponent(ref rotationComposer, framing == CameraFramingMode.LookAtTarget && LookTarget != null);
        SyncComponent(ref splineDolly, positioning == CameraPositioningMode.SplineDolly);
        SyncComponent(
            ref compoundConfiner,
            positioning == CameraPositioningMode.FollowTarget && confinement == CameraConfinementMode.ConfineToRoomZone);
        SyncComponent(
            ref deoccluder,
            positioning != CameraPositioningMode.Static
            && occlusion == CameraOcclusionMode.DeoccludeTarget
            && TrackingTarget != null);
    }

    private void SyncConfinerVolumes()
    {
        if (!compoundConfiner)
            return;

        // The zone authors the room shape. The confiner just consumes that collider list.
        var boundingVolumes = RoomZone ? RoomZone.GetConfinementVolumes() : null;
        if (!compoundConfiner.SetBoundingVolumes(boundingVolumes))
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(compoundConfiner);
#endif
    }

    private void SyncComponent<T>(ref T component, bool shouldExist) where T : Behaviour
    {
        if (shouldExist)
        {
            EnsureComponent(ref component).enabled = true;
            return;
        }

        RemoveComponent(ref component);
    }

    private T EnsureComponent<T>(ref T component) where T : Component
    {
        if (component)
            return component;

        if (TryGetComponent(out component))
            return component;

        component = gameObject.AddComponent<T>();
        return component;
    }

    private void RemoveComponent<T>(ref T component) where T : Component
    {
        if (!component && !TryGetComponent(out component))
            return;

        if (Application.isPlaying)
            Destroy(component);
        else
            DestroyImmediate(component);

        component = null;
    }

    private RoomCameraZone FindRoomZone()
    {
        if (roomZone)
            return roomZone;

        if (transform.parent)
        {
            roomZone = transform.parent.GetComponentInChildren<RoomCameraZone>(true);
            if (roomZone)
                return roomZone;
        }

        return roomZone = GetComponentInChildren<RoomCameraZone>(true);
    }

    internal void AssignRoomZone(RoomCameraZone zone)
    {
        roomZone = zone;
    }

    internal void RequestConfigurationRefresh()
    {
        MarkDirty();

        if (Application.isPlaying)
        {
            RefreshRuntimeBindings();
            return;
        }

#if UNITY_EDITOR
        QueueEditorRefresh();
#endif
    }

#if UNITY_EDITOR
    [ContextMenu("Presets/Static")]
    private void ApplyStaticPreset() => ApplyPreset(
        "Static",
        CameraPositioningMode.Static,
        CameraFramingMode.None,
        CameraConfinementMode.None,
        CameraOcclusionMode.None);

    [ContextMenu("Presets/Tracking")]
    private void ApplyTrackingPreset() => ApplyPreset(
        "Tracking",
        CameraPositioningMode.FollowTarget,
        CameraFramingMode.LookAtTarget,
        CameraConfinementMode.None,
        CameraOcclusionMode.None);

    [ContextMenu("Presets/Confined")]
    private void ApplyConfinedPreset() => ApplyPreset(
        "Confined",
        CameraPositioningMode.FollowTarget,
        CameraFramingMode.LookAtTarget,
        CameraConfinementMode.ConfineToRoomZone,
        CameraOcclusionMode.DeoccludeTarget);

    [ContextMenu("Presets/Dolly")]
    private void ApplyDollyPreset() => ApplyPreset(
        "Dolly",
        CameraPositioningMode.SplineDolly,
        CameraFramingMode.LookAtTarget,
        CameraConfinementMode.None,
        CameraOcclusionMode.None);

    private void ApplyPreset(
        string presetName,
        CameraPositioningMode newPositioning,
        CameraFramingMode newFraming,
        CameraConfinementMode newConfinement,
        CameraOcclusionMode newOcclusion)
    {
        UnityEditor.Undo.RecordObject(this, $"Apply {presetName} Preset");
        positioning = newPositioning;
        framing = newFraming;
        confinement = newConfinement;
        occlusion = newOcclusion;
        RequestConfigurationRefresh();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    private void QueueEditorRefresh()
    {
        if (Application.isPlaying || editorRefreshQueued)
            return;

        editorRefreshQueued = true;
        UnityEditor.EditorApplication.delayCall += RunQueuedEditorRefresh;
    }

    private void RunQueuedEditorRefresh()
    {
        editorRefreshQueued = false;
        if (this)
            RefreshConfiguration();
    }

    [Button("Create Linked Room Zone", editModeOnly: true, visibleIf: nameof(HasLinkedRoomZone), invertVisible: true)]
    private void CreateRoomZone()
    {
        var zoneObject = CreateObject($"{name} Zone", transform.parent, transform.position, true);
        zoneObject.transform.localRotation = Quaternion.identity;
        zoneObject.transform.localScale = Vector3.one;

        var triggerRoot = CreateChild("Trigger Volumes", zoneObject.transform);
        var triggerVolume = CreateChild("Trigger Volume", triggerRoot.transform);
        var triggerCollider = UnityEditor.Undo.AddComponent<BoxCollider>(triggerVolume);
        triggerCollider.isTrigger = true;

        var confinementRoot = CreateChild("Confinement Volumes", zoneObject.transform);
        var confinementVolume = CreateChild("Confinement Volume", confinementRoot.transform);
        var boundsCollider = UnityEditor.Undo.AddComponent<BoxCollider>(confinementVolume);
        boundsCollider.center = triggerCollider.center;
        boundsCollider.size = triggerCollider.size;

        var zone = UnityEditor.Undo.AddComponent<RoomCameraZone>(zoneObject);
        zone.SetTriggerVolumesRoot(triggerRoot.transform);
        zone.SetConfinementVolumesRoot(confinementRoot.transform);
        zone.SetRoomCamera(this);
        zone.SyncZone();

        UnityEditor.Selection.activeGameObject = zoneObject;
        UnityEditor.EditorGUIUtility.PingObject(zoneObject);
    }

    [Button("Select Linked Room Zone", editModeOnly: true, visibleIf: nameof(HasLinkedRoomZone))]
    private void SelectLinkedRoomZone()
    {
        if (!RoomZone)
            return;

        UnityEditor.Selection.activeGameObject = RoomZone.gameObject;
        UnityEditor.EditorGUIUtility.PingObject(RoomZone.gameObject);
    }

    private static GameObject CreateChild(string childName, Transform parent)
    {
        return CreateObject(childName, parent, Vector3.zero, false);
    }

    private static GameObject CreateObject(string objectName, Transform parent, Vector3 position, bool keepWorldPosition)
    {
        var gameObject = new GameObject(objectName);
        UnityEditor.Undo.RegisterCreatedObjectUndo(gameObject, $"Create {objectName}");
        gameObject.transform.SetParent(parent, keepWorldPosition);
        if (keepWorldPosition)
            gameObject.transform.position = position;
        else
            gameObject.transform.localPosition = position;

        gameObject.transform.localScale = Vector3.one;
        return gameObject;
    }
#endif
}
