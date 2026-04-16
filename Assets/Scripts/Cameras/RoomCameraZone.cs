using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Camera/Room Camera Zone")]
[RequireComponent(typeof(CinemachineTriggerAction))]
public class RoomCameraZone : MonoBehaviour
{
    private static readonly List<RoomCameraZone> RuntimeZones = new();
    private static RoomCameraZone activeRuntimeZone;
    private static int entrySequence;

    [FieldHeader("Room Link")]
    [SerializeField] private RoomCameraController linkedRoomCamera;
    [SerializeField] private bool deactivateCameraOnExit = true;

    [FieldHeader("Activation")]
    [SerializeField] private Transform triggerVolumesRoot;

    [FieldHeader("Confinement")]
    [SerializeField] private Transform confinementVolumesRoot;

    private readonly Dictionary<GameObject, int> occupantCounts = new();
    private CinemachineTriggerAction triggerAction;
    private bool isOccupied;
    private int lastEnteredOrder = -1;

    public RoomCameraController RoomCamera => linkedRoomCamera ? linkedRoomCamera : linkedRoomCamera = FindRoomCamera();

    private CinemachineTriggerAction TriggerAction => triggerAction ? triggerAction : triggerAction = GetComponent<CinemachineTriggerAction>();
    private Transform TriggerVolumesRoot => triggerVolumesRoot ? triggerVolumesRoot : triggerVolumesRoot = FindRoot("Trigger Volumes", "Triggers");
    private Transform ConfinementVolumesRoot => confinementVolumesRoot ? confinementVolumesRoot : confinementVolumesRoot = FindRoot("Confinement Volumes", "Bounds");
    private bool HasLinkedRoomCamera => RoomCamera != null;

    public void SetRoomCamera(RoomCameraController controller, bool syncCamera = true)
    {
        linkedRoomCamera = controller;
        if (syncCamera && controller != null)
            controller.AssignRoomZone(this);

        RequestCameraRefresh();
    }

    public void SetTriggerVolumesRoot(Transform root)
    {
        triggerVolumesRoot = root;
        SyncZone();
    }

    public void SetConfinementVolumesRoot(Transform root)
    {
        confinementVolumesRoot = root;
        SyncZone();
    }

    public Collider[] GetTriggerVolumes()
    {
        return GetChildColliders(TriggerVolumesRoot, true);
    }

    public Collider[] GetConfinementVolumes()
    {
        return GetChildColliders(ConfinementVolumesRoot, false);
    }

    [Button("Sync Room Zone", editModeOnly: true)]
    public void SyncZone()
    {
        RefreshZoneSetup(addTriggerRelays: true);
    }

    private void Reset()
    {
        triggerAction = GetComponent<CinemachineTriggerAction>();
        linkedRoomCamera = FindRoomCamera();
        triggerVolumesRoot = FindRoot("Trigger Volumes", "Triggers");
        confinementVolumesRoot = FindRoot("Confinement Volumes", "Bounds");
        SyncZone();
    }

    private void OnEnable()
    {
        RefreshZoneSetup(addTriggerRelays: true);

        if (!Application.isPlaying)
            return;

        RegisterRuntimeZone(this);
    }

    private void Start()
    {
        if (!Application.isPlaying)
            return;

        CaptureInitialOccupants();
        EvaluateRuntimeZones();
    }

    private void OnValidate()
    {
        triggerAction = GetComponent<CinemachineTriggerAction>();
        triggerVolumesRoot ??= FindRoot("Trigger Volumes", "Triggers");
        confinementVolumesRoot ??= FindRoot("Confinement Volumes", "Bounds");
        RefreshZoneSetup(addTriggerRelays: false);
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            return;

        occupantCounts.Clear();
        isOccupied = false;
        lastEnteredOrder = -1;

        UnregisterRuntimeZone(this);
        if (activeRuntimeZone == this)
            activeRuntimeZone = null;

        if (deactivateCameraOnExit)
            RoomCamera?.DeactivateRoom();

        EvaluateRuntimeZones();
    }

    private void RefreshZoneSetup(bool addTriggerRelays)
    {
        ConfigureTriggerAction();

        if (!linkedRoomCamera)
            linkedRoomCamera = FindRoomCamera();
        if (!triggerVolumesRoot)
            triggerVolumesRoot = FindRoot("Trigger Volumes", "Triggers");
        if (!confinementVolumesRoot)
            confinementVolumesRoot = FindRoot("Confinement Volumes", "Bounds");

        SetCollidersAsTriggers(GetChildColliders(TriggerVolumesRoot), true);
        SetCollidersAsTriggers(GetChildColliders(ConfinementVolumesRoot), false);

        if (linkedRoomCamera)
            linkedRoomCamera.AssignRoomZone(this);

        if (addTriggerRelays)
            EnsureTriggerRelays();

        RequestCameraRefresh();
    }

    private void EnsureTriggerRelays()
    {
        var triggerVolumes = GetTriggerVolumes();
        for (var i = 0; i < triggerVolumes.Length; i++)
        {
            var triggerVolume = triggerVolumes[i];
            if (!triggerVolume)
                continue;

            var relay = triggerVolume.GetComponent<RoomCameraZoneTriggerRelay>();
            if (!relay)
            {
#if UNITY_EDITOR
                relay = Application.isPlaying
                    ? triggerVolume.gameObject.AddComponent<RoomCameraZoneTriggerRelay>()
                    : UnityEditor.Undo.AddComponent<RoomCameraZoneTriggerRelay>(triggerVolume.gameObject);
#else
                relay = triggerVolume.gameObject.AddComponent<RoomCameraZoneTriggerRelay>();
#endif
            }

            relay.SetZone(this);
        }
    }

    private static void RegisterRuntimeZone(RoomCameraZone zone)
    {
        if (!zone || RuntimeZones.Contains(zone))
            return;

        RuntimeZones.Add(zone);
    }

    private static void UnregisterRuntimeZone(RoomCameraZone zone)
    {
        if (!zone)
            return;

        RuntimeZones.Remove(zone);
    }

    private static void EvaluateRuntimeZones()
    {
        var nextActiveZone = ChooseRuntimeActiveZone();
        if (nextActiveZone == activeRuntimeZone)
        {
            var currentRoomCamera = nextActiveZone ? nextActiveZone.RoomCamera : null;
            if (currentRoomCamera != null && !currentRoomCamera.Active)
                currentRoomCamera.ActivateRoom();

            return;
        }

        var previousActiveZone = activeRuntimeZone;
        activeRuntimeZone = nextActiveZone;

        if (previousActiveZone != null && previousActiveZone != nextActiveZone && previousActiveZone.deactivateCameraOnExit)
            previousActiveZone.RoomCamera?.DeactivateRoom();

        if (nextActiveZone != null)
            nextActiveZone.RoomCamera?.ActivateRoom();
    }

    private static RoomCameraZone ChooseRuntimeActiveZone()
    {
        RoomCameraZone bestZone = null;
        var bestEntryOrder = int.MinValue;

        for (var i = 0; i < RuntimeZones.Count; i++)
        {
            var zone = RuntimeZones[i];
            if (!zone || !zone.isActiveAndEnabled || !zone.isOccupied)
                continue;
            if (zone.lastEnteredOrder <= bestEntryOrder)
                continue;

            bestZone = zone;
            bestEntryOrder = zone.lastEnteredOrder;
        }

        return bestZone;
    }

    private void CaptureInitialOccupants()
    {
        occupantCounts.Clear();
        isOccupied = false;

        var triggerVolumes = GetTriggerVolumes();
        if (triggerVolumes.Length == 0)
            return;

        var trackedObjects = FindTrackedObjects();
        for (var i = 0; i < trackedObjects.Length; i++)
        {
            var trackedObject = trackedObjects[i];
            if (!PassesTriggerActionFilter(trackedObject))
                continue;

            var overlapCount = CountInitialOverlaps(trackedObject, triggerVolumes);
            if (overlapCount <= 0)
                continue;

            occupantCounts[trackedObject] = overlapCount;
            isOccupied = true;
            lastEnteredOrder = ++entrySequence;
        }
    }

    private int CountInitialOverlaps(GameObject trackedObject, Collider[] triggerVolumes)
    {
        var trackedColliders = trackedObject.GetComponentsInChildren<Collider>(true);
        if (trackedColliders == null || trackedColliders.Length == 0)
            return 0;

        var overlapCount = 0;
        for (var i = 0; i < triggerVolumes.Length; i++)
        {
            var triggerVolume = triggerVolumes[i];
            if (!IsUsableCollider(triggerVolume))
                continue;

            for (var j = 0; j < trackedColliders.Length; j++)
            {
                var trackedCollider = trackedColliders[j];
                if (!IsTrackedCollider(trackedCollider))
                    continue;
                if (!AreCollidersOverlapping(triggerVolume, trackedCollider))
                    continue;

                overlapCount++;
            }
        }

        return overlapCount;
    }

    internal void NotifyTriggerEnter(Collider other)
    {
        if (!Application.isPlaying)
            return;
        if (!TryGetTrackedObject(other, out var trackedObject))
            return;
        if (!PassesTriggerActionFilter(trackedObject))
            return;

        UpdateOccupantCount(trackedObject, +1);
    }

    internal void NotifyTriggerExit(Collider other)
    {
        if (!Application.isPlaying)
            return;
        if (!TryGetTrackedObject(other, out var trackedObject))
            return;
        if (!PassesTriggerActionFilter(trackedObject))
            return;

        UpdateOccupantCount(trackedObject, -1);
    }

    private void UpdateOccupantCount(GameObject trackedObject, int delta)
    {
        if (!trackedObject || delta == 0)
            return;

        occupantCounts.TryGetValue(trackedObject, out var overlapCount);
        overlapCount = Mathf.Max(0, overlapCount + delta);
        if (overlapCount > 0)
            occupantCounts[trackedObject] = overlapCount;
        else
            occupantCounts.Remove(trackedObject);

        var wasOccupied = isOccupied;
        isOccupied = occupantCounts.Count > 0;
        if (delta > 0 && overlapCount == 1)
            lastEnteredOrder = ++entrySequence;
        if (wasOccupied && !isOccupied)
            lastEnteredOrder = -1;

        EvaluateRuntimeZones();
    }

    private void ConfigureTriggerAction()
    {
        var action = TriggerAction;
        action.enabled = true;
        action.OnObjectEnter = ConfigureAction(action.OnObjectEnter, linkedRoomCamera);
        action.OnObjectExit = ConfigureAction(action.OnObjectExit, linkedRoomCamera);

#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(action);
#endif
    }

    private bool PassesTriggerActionFilter(GameObject other)
    {
        var action = TriggerAction;
        if (!other || !other.activeInHierarchy)
            return false;
        if (!action.enabled)
            return false;
        if (((1 << other.layer) & action.LayerMask.value) == 0)
            return false;
        if (!string.IsNullOrEmpty(action.WithTag) && !other.CompareTag(action.WithTag))
            return false;
        if (!string.IsNullOrEmpty(action.WithoutTag) && other.CompareTag(action.WithoutTag))
            return false;

        return true;
    }

    private static bool TryGetTrackedObject(Collider other, out GameObject trackedObject)
    {
        trackedObject = null;

        if (!IsTrackedCollider(other))
            return false;

        if (other.attachedRigidbody)
            trackedObject = other.attachedRigidbody.gameObject;
        else if (other.transform.root)
            trackedObject = other.transform.root.gameObject;

        return trackedObject != null;
    }

    private GameObject[] FindTrackedObjects()
    {
        var withTag = TriggerAction.WithTag;
        if (!string.IsNullOrEmpty(withTag))
            return GameObject.FindGameObjectsWithTag(withTag);

        var rigidbodies = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
        if (rigidbodies.Length > 0)
            return CollectRigidbodies(rigidbodies);

        var colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        return CollectColliders(colliders);
    }

    private static bool AreCollidersOverlapping(Collider first, Collider second)
    {
        if (!IsUsableCollider(first) || !IsTrackedCollider(second))
            return false;

        if (Physics.ComputePenetration(
                first,
                first.transform.position,
                first.transform.rotation,
                second,
                second.transform.position,
                second.transform.rotation,
                out _,
                out _))
            return true;

        return first.bounds.Intersects(second.bounds);
    }

    private static bool IsUsableCollider(Collider collider)
    {
        return collider && collider.enabled && collider.gameObject.activeInHierarchy;
    }

    private static bool IsTrackedCollider(Collider collider)
    {
        return collider && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy;
    }

    private void RequestCameraRefresh()
    {
        if (linkedRoomCamera)
            linkedRoomCamera.RequestConfigurationRefresh();
    }

    private RoomCameraController FindRoomCamera()
    {
        if (linkedRoomCamera)
            return linkedRoomCamera;

        if (transform.parent)
        {
            linkedRoomCamera = transform.parent.GetComponentInChildren<RoomCameraController>(true);
            if (linkedRoomCamera)
                return linkedRoomCamera;
        }

        return linkedRoomCamera = GetComponentInChildren<RoomCameraController>(true);
    }

    private Transform FindRoot(string primaryName, string fallbackName)
    {
        var root = transform.Find(primaryName);
        return root ? root : transform.Find(fallbackName);
    }

    private static void SetCollidersAsTriggers(Collider[] colliders, bool isTrigger)
    {
        for (var i = 0; i < colliders.Length; i++)
            colliders[i].isTrigger = isTrigger;
    }

    private Collider[] GetChildColliders(Transform root, bool? isTrigger = null)
    {
        if (!root)
            return Array.Empty<Collider>();

        var foundColliders = root.GetComponentsInChildren<Collider>(true);
        if (foundColliders == null || foundColliders.Length == 0)
            return Array.Empty<Collider>();

        var colliders = new List<Collider>(foundColliders.Length);
        for (var i = 0; i < foundColliders.Length; i++)
        {
            var collider = foundColliders[i];
            if (!collider || collider.transform == transform)
                continue;
            if (isTrigger.HasValue && collider.isTrigger != isTrigger.Value)
                continue;

            colliders.Add(collider);
        }

        return colliders.Count == 0 ? Array.Empty<Collider>() : colliders.ToArray();
    }

    private static GameObject[] CollectRigidbodies(Rigidbody[] rigidbodies)
    {
        if (rigidbodies == null || rigidbodies.Length == 0)
            return Array.Empty<GameObject>();

        var objects = new List<GameObject>(rigidbodies.Length);
        for (var i = 0; i < rigidbodies.Length; i++)
        {
            var rigidbody = rigidbodies[i];
            if (!rigidbody)
                continue;

            objects.Add(rigidbody.gameObject);
        }

        return objects.Count == 0 ? Array.Empty<GameObject>() : objects.ToArray();
    }

    private static GameObject[] CollectColliders(Collider[] colliders)
    {
        if (colliders == null || colliders.Length == 0)
            return Array.Empty<GameObject>();

        var objects = new List<GameObject>(colliders.Length);
        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (!collider)
                continue;
            if (objects.Contains(collider.gameObject))
                continue;

            objects.Add(collider.gameObject);
        }

        return objects.Count == 0 ? Array.Empty<GameObject>() : objects.ToArray();
    }

    private static CinemachineTriggerAction.ActionSettings ConfigureAction(
        CinemachineTriggerAction.ActionSettings actionSettings,
        RoomCameraController roomCamera)
    {
        actionSettings.Action = CinemachineTriggerAction.ActionSettings.ActionModes.EventOnly;
        actionSettings.Target = roomCamera ? roomCamera.gameObject : null;
        actionSettings.Event ??= new CinemachineTriggerAction.ActionSettings.TriggerEvent();
        return actionSettings;
    }

    internal void AssignRoomCamera(RoomCameraController controller)
    {
        linkedRoomCamera = controller;
    }

#if UNITY_EDITOR
    [Button("Add Trigger Box", editModeOnly: true)]
    private void AddTriggerBox()
    {
        var root = EnsureEditorRoot(ref triggerVolumesRoot, "Trigger Volumes");
        var boxCollider = CreateVolumeBox(root, "Trigger Volume", true);
        boxCollider.size = new Vector3(6f, 3f, 6f);
        CompleteVolumeCreation(boxCollider.gameObject);
    }

    [Button("Add Confinement Box", editModeOnly: true)]
    private void AddConfinementBox()
    {
        var root = EnsureEditorRoot(ref confinementVolumesRoot, "Confinement Volumes");
        var boxCollider = CreateVolumeBox(root, "Confinement Volume", false);
        boxCollider.size = new Vector3(8f, 4f, 8f);
        CompleteVolumeCreation(boxCollider.gameObject);
    }

    [Button("Create Linked Room Camera", editModeOnly: true, visibleIf: nameof(HasLinkedRoomCamera), invertVisible: true)]
    private void CreateLinkedRoomCamera()
    {
        var cameraObject = new GameObject(GetSuggestedCameraName());
        UnityEditor.Undo.RegisterCreatedObjectUndo(cameraObject, "Create Room Camera");
        cameraObject.transform.SetParent(transform.parent, true);
        cameraObject.transform.position = transform.position;
        cameraObject.transform.localRotation = Quaternion.identity;
        cameraObject.transform.localScale = Vector3.one;

        var controller = UnityEditor.Undo.AddComponent<RoomCameraController>(cameraObject);
        controller.SetRoomZone(this);
        controller.RefreshConfiguration();

        UnityEditor.Selection.activeGameObject = cameraObject;
        UnityEditor.EditorGUIUtility.PingObject(cameraObject);
    }

    [Button("Select Linked Room Camera", editModeOnly: true, visibleIf: nameof(HasLinkedRoomCamera))]
    private void SelectRoomCamera()
    {
        if (!RoomCamera)
            return;

        UnityEditor.Selection.activeGameObject = RoomCamera.gameObject;
        UnityEditor.EditorGUIUtility.PingObject(RoomCamera.gameObject);
    }

    private string GetSuggestedCameraName()
    {
        const string zoneSuffix = " Zone";
        return name.EndsWith(zoneSuffix, StringComparison.Ordinal)
            ? $"{name.Substring(0, name.Length - zoneSuffix.Length)} Camera"
            : $"{name} Camera";
    }

    private Transform EnsureEditorRoot(ref Transform root, string rootName)
    {
        if (!root)
            root = transform.Find(rootName);
        if (root)
            return root;

        var rootObject = new GameObject(rootName);
        UnityEditor.Undo.RegisterCreatedObjectUndo(rootObject, $"Create {rootName}");
        rootObject.transform.SetParent(transform, false);
        rootObject.transform.localPosition = Vector3.zero;
        rootObject.transform.localRotation = Quaternion.identity;
        rootObject.transform.localScale = Vector3.one;

        root = rootObject.transform;
        UnityEditor.EditorUtility.SetDirty(this);
        return root;
    }

    private BoxCollider CreateVolumeBox(Transform root, string baseName, bool isTrigger)
    {
        var volumeObject = new GameObject(GetNextVolumeName(root, baseName));
        UnityEditor.Undo.RegisterCreatedObjectUndo(volumeObject, $"Create {baseName}");
        volumeObject.transform.SetParent(root, false);
        volumeObject.transform.localPosition = Vector3.zero;
        volumeObject.transform.localRotation = Quaternion.identity;
        volumeObject.transform.localScale = Vector3.one;

        var boxCollider = UnityEditor.Undo.AddComponent<BoxCollider>(volumeObject);
        boxCollider.isTrigger = isTrigger;
        return boxCollider;
    }

    private static string GetNextVolumeName(Transform root, string baseName)
    {
        if (!root)
            return baseName;

        var nextIndex = root.childCount + 1;
        return nextIndex <= 1 ? baseName : $"{baseName} {nextIndex}";
    }

    private void CompleteVolumeCreation(GameObject volumeObject)
    {
        SyncZone();
        UnityEditor.Selection.activeGameObject = volumeObject;
        UnityEditor.EditorGUIUtility.PingObject(volumeObject);
    }
#endif
}
