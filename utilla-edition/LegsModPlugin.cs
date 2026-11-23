using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

[BepInPlugin("purplemoonieee.leg.mod.utilla", "Legs Mod", "1.0.9")]
[BepInDependency("org.legoandmars.gorillatag.utilla", "1.5.0")]
public sealed class ModSidedLegArmsPlugin : BaseUnityPlugin
{
    private static ModSidedLegArmsBehaviour activeBehaviour;
    private Harmony harmony;

    private void Awake()
    {
#if !LEGS_ALWAYS_ON
        ConfigEntry<KeyboardShortcut> toggleKey = Config.Bind("General", "ToggleLegsKey",
            new KeyboardShortcut(KeyCode.F8),
            new ConfigDescription("Keyboard fallback for toggling leg visuals."));
#endif
        ConfigEntry<float> shoulderDrop = Config.Bind("Visuals", "ShoulderDropMeters", 0.35f,
            new ConfigDescription("Move the rendered shoulders and hands down.",
                new AcceptableValueRange<float>(0f, 0.5f)));
        ConfigEntry<float> handRaise = Config.Bind("Visuals", "HandRaiseMeters", 0.0290f,
            new ConfigDescription("Raise the rendered hands relative to their rig collider anchors.",
                new AcceptableValueRange<float>(-0.25f, 0.25f)));
        ConfigEntry<float> handTowardTorso = Config.Bind("Visuals", "HandTowardTorsoMeters", 0.08f,
            new ConfigDescription("Move rendered hands back toward the torso.",
                new AcceptableValueRange<float>(0f, 0.2f)));
        ConfigEntry<bool> alignHandsAtKeyboard = Config.Bind("Interaction", "AlignRenderedHandsAtComputerKeyboard", true,
            new ConfigDescription("Restore the original rendered hand and shoulder positions near the computer keyboard."));
        ConfigEntry<bool> restoreHandsAtWardrobe = Config.Bind("Interaction", "RestoreRenderedHandsAtCosmeticWardrobe", true,
            new ConfigDescription("Restore the original rendered hand and shoulder positions near the owned-cosmetics wardrobe and its equip buttons."));

        ModSidedLegArmsBehaviour behaviour = gameObject.AddComponent<ModSidedLegArmsBehaviour>();
#if !LEGS_ALWAYS_ON
        behaviour.ToggleKey = toggleKey;
#endif
        behaviour.ShoulderDrop = shoulderDrop;
        behaviour.HandRaise = handRaise;
        behaviour.HandTowardTorso = handTowardTorso;
        behaviour.AlignHandsAtKeyboard = alignHandsAtKeyboard;
        behaviour.RestoreHandsAtWardrobe = restoreHandsAtWardrobe;
        behaviour.Log = Logger;
        activeBehaviour = behaviour;
        InstallThrowableHooks();
    }

    private void InstallThrowableHooks()
    {
        try
        {
            MethodInfo grabbed = typeof(GorillaThrowable).GetMethod("Grabbed",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Transform) }, null);
            MethodInfo thrown = typeof(GorillaThrowable).GetMethod("ThrowThisThingo",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            harmony = new Harmony("purplemoonieee.leg.mod.heldthrowable");
            if (grabbed != null && thrown != null)
            {
                MethodInfo grabbedPostfix = typeof(ModSidedLegArmsPlugin).GetMethod(
                    nameof(AfterThrowableGrabbed), BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo throwPrefix = typeof(ModSidedLegArmsPlugin).GetMethod(
                    nameof(BeforeThrowableThrown), BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo throwPostfix = typeof(ModSidedLegArmsPlugin).GetMethod(
                    nameof(AfterThrowableThrown), BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(grabbed, postfix: new HarmonyMethod(grabbedPostfix));
                harmony.Patch(thrown, prefix: new HarmonyMethod(throwPrefix),
                    postfix: new HarmonyMethod(throwPostfix));
            }
            else
                Logger.LogWarning("Could not find GorillaThrowable grab/throw methods; using the generic held-item path where available.");

            MethodInfo snowballAnchor = typeof(SnowballThrowable).GetMethod("AnchorToHand",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (snowballAnchor != null)
            {
                MethodInfo snowballAnchorPostfix = typeof(ModSidedLegArmsPlugin).GetMethod(
                    nameof(AfterSnowballAnchored), BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(snowballAnchor, postfix: new HarmonyMethod(snowballAnchorPostfix));
            }
            else
                Logger.LogWarning("Could not find SnowballThrowable.AnchorToHand(); held snowball positioning will use the game's default offset.");

            MethodInfo releaseLeft = typeof(EquipmentInteractor).GetMethod("ReleaseLeftHand",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            MethodInfo releaseRight = typeof(EquipmentInteractor).GetMethod("ReleaseRightHand",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            MethodInfo releasePrefix = typeof(ModSidedLegArmsPlugin).GetMethod(
                nameof(BeforeEquipmentRelease), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo releasePostfix = typeof(ModSidedLegArmsPlugin).GetMethod(
                nameof(AfterEquipmentRelease), BindingFlags.Static | BindingFlags.NonPublic);
            if (releaseLeft != null)
                harmony.Patch(releaseLeft, prefix: new HarmonyMethod(releasePrefix),
                    postfix: new HarmonyMethod(releasePostfix));
            if (releaseRight != null)
                harmony.Patch(releaseRight, prefix: new HarmonyMethod(releasePrefix),
                    postfix: new HarmonyMethod(releasePostfix));

            Logger.LogInfo("Installed local held-item grab and release hooks for both hands.");
        }
        catch (Exception exception)
        {
            Logger.LogWarning("Could not install immediate held-projectile hooks: " + exception.Message);
        }
    }

    private static void AfterThrowableGrabbed(GorillaThrowable __instance, Transform __0)
    {
        if (activeBehaviour != null)
            activeBehaviour.BindThrowableImmediately(__instance, __0);
    }

    private static void BeforeThrowableThrown(GorillaThrowable __instance)
    {
        if (activeBehaviour != null)
            activeBehaviour.PrepareThrowableThrow(__instance);
    }

    private static void AfterThrowableThrown(GorillaThrowable __instance)
    {
        if (activeBehaviour != null)
            activeBehaviour.FinishThrowableThrow(__instance);
    }

    private static void AfterSnowballAnchored(SnowballThrowable __instance)
    {
        if (activeBehaviour != null)
            activeBehaviour.ApplyHeldSnowballVisualOffset(__instance);
    }

    private static void BeforeEquipmentRelease(EquipmentInteractor __instance,
        MethodBase __originalMethod, ref IHoldableObject __state)
    {
        bool leftHand = __originalMethod != null
            && __originalMethod.Name == "ReleaseLeftHand";
        __state = __instance == null ? null
            : (leftHand ? __instance.leftHandHeldEquipment : __instance.rightHandHeldEquipment);
        if (activeBehaviour != null)
            activeBehaviour.PrepareEquipmentRelease(__instance, __state);
    }

    private static void AfterEquipmentRelease(IHoldableObject __state)
    {
        if (activeBehaviour != null)
            activeBehaviour.FinishEquipmentRelease(__state);
    }

    private void OnDestroy()
    {
        if (harmony != null)
        {
            harmony.UnpatchSelf();
            harmony = null;
        }
        if (activeBehaviour != null && activeBehaviour.Log == Logger)
            activeBehaviour = null;
    }
}

[DefaultExecutionOrder(10000)]
public sealed class ModSidedLegArmsBehaviour : MonoBehaviourPunCallbacks
{
    private const string PresenceKey = "purplemoonieee_legs_visual_v1";
    private const string LegacyPresenceKey = "codex_legs_visual_v1";
    private const string PresenceValue = "1";
    private const string DisabledPresenceValue = "0";
    private const float LookupInterval = 1f;
    private const float HeldThrowableLookupInterval = 0.2f;
    private const float HeldThrowableHandMatchRange = 0.9f;
    private static readonly Vector3 HeldProjectileVisualOffset = new Vector3(0f, -0.035f, 0.04f);
    private static readonly FieldInfo ThrowableFollowTransformField =
        typeof(GorillaThrowable).GetField("transformToFollow",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo SnowballAnchorToHandMethod =
        FindInstanceMethod(typeof(SnowballThrowable), "AnchorToHand");
    private static readonly FieldInfo SnowballTargetRigField =
        FindInstanceField(typeof(SnowballThrowable), "targetRig");
    private static readonly PropertyInfo SnowballTargetRigProperty =
        FindInstanceProperty(typeof(SnowballThrowable), "targetRig");
    private static readonly FieldInfo SnowballAnchorField =
        FindInstanceField(typeof(SnowballThrowable), "anchor");

    public ConfigEntry<float> ShoulderDrop;
    public ConfigEntry<float> HandRaise;
    public ConfigEntry<float> HandTowardTorso;
    public ConfigEntry<bool> AlignHandsAtKeyboard;
    public ConfigEntry<bool> RestoreHandsAtWardrobe;
#if !LEGS_ALWAYS_ON
    public ConfigEntry<KeyboardShortcut> ToggleKey;
#endif
    public ManualLogSource Log;

    private readonly Dictionary<VRRig, RigState> states = new Dictionary<VRRig, RigState>();
    private readonly List<VRRig> staleRigs = new List<VRRig>();
    private readonly List<Collider> computerKeyColliders = new List<Collider>();
    private readonly List<Transform> computerTerminalTransforms = new List<Transform>();
    private readonly List<Transform> cosmeticWardrobeAnchors = new List<Transform>();
    private readonly List<HeldEquipmentBinding> heldEquipmentBindings =
        new List<HeldEquipmentBinding>();
    private readonly List<HeldThrowableBinding> dormantThrowableBindings =
        new List<HeldThrowableBinding>();
    private float nextComputerKeyLookupTime;
    private float nextHeldThrowableLookupTime;
    private float nextEquipmentInteractorLookupTime;
    private string lastLoggedInteractionTargetCounts;
    private EquipmentInteractor equipmentInteractor;
    private bool loggedMissingEquipmentInteractor;
    private bool loggedEquipmentInteractorFound;
    private bool loggedMissingSnowballAnchorMethod;
    private float nextPresenceAttemptTime;
    private bool presenceConfirmed;
    private bool legsEnabled = true;
#if !LEGS_ALWAYS_ON
    private ControllerInputPoller controllerPoller;
    private float nextControllerPollerLookupTime;
    private bool loggedControllerPollerFound;
    private bool gamePrimaryButtonWasPressed;
#endif
    private bool wasInModdedLobby;
#if !LEGS_ALWAYS_ON
    private bool loggedToggleInputDiagnostic;
#endif

    private sealed class RigState
    {
        public Transform LeftShoulder;
        public Transform RightShoulder;
        public Vector3 LeftShoulderOriginal;
        public Vector3 RightShoulderOriginal;
        public bool HasOriginalShoulders;
        public bool LoggedMissingShoulders;
        public bool LoggedVisualRig;
        public bool VisualRigReady;
        public float NextLookupTime;
        public readonly List<HandProxy> HandProxies = new List<HandProxy>();
        public readonly List<RendererBoneBackup> RendererBackups = new List<RendererBoneBackup>();
        public readonly List<BodyDockPositionBinding> BodyDockBindings =
            new List<BodyDockPositionBinding>();
        public readonly List<HeldThrowableBinding> HeldThrowableBindings =
            new List<HeldThrowableBinding>();
    }

    public override void OnJoinedRoom()
    {
        wasInModdedLobby = false;
        presenceConfirmed = false;
        nextPresenceAttemptTime = 0f;
        if (legsEnabled)
            AnnouncePresence();
    }

    public override void OnLeftRoom()
    {
        wasInModdedLobby = false;
        presenceConfirmed = false;
        nextPresenceAttemptTime = 0f;
        RestoreAllRigs();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (otherPlayer == null)
            return;

        staleRigs.Clear();
        foreach (KeyValuePair<VRRig, RigState> pair in states)
        {
            VRRig rig = pair.Key;
            NetPlayer owner = rig != null ? rig.Creator : null;
            if (rig != null && rig != VRRig.LocalRig && owner != null
                && owner.ActorNumber == otherPlayer.ActorNumber)
                staleRigs.Add(rig);
        }
        for (int i = 0; i < staleRigs.Count; i++)
            RestoreRig(staleRigs[i]);
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (targetPlayer == null || changedProps == null
            || (!changedProps.ContainsKey(PresenceKey) && !changedProps.ContainsKey(LegacyPresenceKey)))
            return;

        Player localPlayer = PhotonNetwork.LocalPlayer;
        bool isLocalPlayer = localPlayer != null && targetPlayer.ActorNumber == localPlayer.ActorNumber;
        bool markerPresent = HasPresenceMarker(targetPlayer);
        if (isLocalPlayer)
            presenceConfirmed = markerPresent;

        if (Log != null)
            Log.LogInfo("Photon presence update: actor=" + targetPlayer.ActorNumber
                + ", local=" + isLocalPlayer + ", markerPresent=" + markerPresent + ".");
    }

    private void Start()
    {
        if (legsEnabled && PhotonNetwork.InRoom)
            AnnouncePresence();
    }

#if !LEGS_ALWAYS_ON
    private void Update()
    {
        bool keyboardToggle = ToggleKey != null && WasToggleKeyPressed(ToggleKey.Value);
        if (controllerPoller == null && Time.unscaledTime >= nextControllerPollerLookupTime)
        {
            nextControllerPollerLookupTime = Time.unscaledTime + 1f;
            controllerPoller = ControllerInputPoller.instance;
            if (controllerPoller == null)
                controllerPoller = FindAnyObjectByType<ControllerInputPoller>();
            if (controllerPoller != null && !loggedControllerPollerFound && Log != null)
            {
                Log.LogInfo("Found Gorilla Tag controller poller; left X toggle is ready.");
                loggedControllerPollerFound = true;
            }
        }
        bool gamePrimaryPressed = controllerPoller != null && controllerPoller.leftControllerPrimaryButton;
        bool controllerToggle = gamePrimaryPressed && !gamePrimaryButtonWasPressed;
        gamePrimaryButtonWasPressed = gamePrimaryPressed;

        if (!loggedToggleInputDiagnostic && Log != null && Time.unscaledTime >= 3f)
        {
            Log.LogInfo("Toggle input diagnostics: Gorilla Tag controller poller=" + (controllerPoller != null)
                + ", left X state=" + gamePrimaryPressed + ", Input System keyboard=" + (Keyboard.current != null) + ".");
            loggedToggleInputDiagnostic = true;
        }

        if (!keyboardToggle && !controllerToggle)
            return;

        legsEnabled = !legsEnabled;
        if (legsEnabled)
            PublishPresence(true);
        else
        {
            PublishPresence(false);
            RestoreAllRigs();
        }

        if (Log != null)
            Log.LogInfo("Leg visuals " + (legsEnabled ? "enabled" : "disabled")
                + ". Toggle with the left controller primary button or the configured keyboard key.");
    }

    private static bool WasToggleKeyPressed(KeyboardShortcut shortcut)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || shortcut.MainKey == KeyCode.None)
            return false;

        string keyName = shortcut.MainKey.ToString();
        if (keyName.StartsWith("Alpha", StringComparison.Ordinal))
            keyName = "Digit" + keyName.Substring("Alpha".Length);
        else if (keyName.StartsWith("Keypad", StringComparison.Ordinal))
            keyName = "Numpad" + keyName.Substring("Keypad".Length);
        else if (keyName == "Return")
            keyName = "Enter";
        else if (keyName == "LeftControl")
            keyName = "LeftCtrl";
        else if (keyName == "RightControl")
            keyName = "RightCtrl";

        Key key;
        if (!Enum.TryParse(keyName, true, out key))
            return false;

        KeyControl control = keyboard[key];
        return control != null && control.wasPressedThisFrame;
    }
#endif

    private void LateUpdate()
    {
        if (!legsEnabled)
        {
            RestoreAllRigs();
            RemoveReleasedHeldEquipmentBindings();
            RemoveDestroyedRigStates();
            return;
        }

        if (!IsInUtillaModdedLobby())
        {
            if (wasInModdedLobby)
                PublishPresence(false);
            wasInModdedLobby = false;
            RestoreAllRigs();
            RemoveDestroyedRigStates();
            return;
        }
        wasInModdedLobby = true;

        RefreshComputerKeyColliders();
        if (PhotonNetwork.InRoom && !presenceConfirmed
            && Time.unscaledTime >= nextPresenceAttemptTime)
            AnnouncePresence();

        Player localPlayer = PhotonNetwork.LocalPlayer;
        if (!presenceConfirmed && PhotonNetwork.InRoom && HasPresenceMarker(localPlayer))
        {
            presenceConfirmed = true;
            if (Log != null)
                Log.LogInfo("Local Photon player properties contain the mod presence marker.");
        }

        VRRig localRig = VRRig.LocalRig;
        if (localRig != null)
            ApplyToRig(localRig);

        if (PhotonNetwork.InRoom)
        {
            IReadOnlyList<VRRig> rigs = VRRigCache.ActiveRigs;
            for (int i = 0; i < rigs.Count; i++)
            {
                VRRig rig = rigs[i];
                if (rig == null || rig == localRig)
                    continue;

                NetPlayer owner = rig.Creator;
                Player photonPlayer = owner != null ? owner.GetPlayerRef() : null;
                if (HasPresenceMarker(photonPlayer))
                {
                    if (!states.ContainsKey(rig) && Log != null)
                        Log.LogInfo("Detected a compatible modded peer (actor "
                            + (photonPlayer != null ? photonPlayer.ActorNumber.ToString() : "unknown") + ").");
                    ApplyToRig(rig);
                }
                else
                    RestoreRig(rig);
            }
        }
        else
        {
            RestoreNonLocalRigs();
        }

        UpdateHeldEquipmentBindings();
        UpdateHeldThrowableBindings();
        RemoveDestroyedRigStates();
    }

    private void AnnouncePresence()
    {
        if (!legsEnabled || !IsPresenceRoomAllowed())
            return;

        Player localPlayer = PhotonNetwork.LocalPlayer;
        if (localPlayer == null)
            return;

        if (HasPresenceMarker(localPlayer))
        {
            presenceConfirmed = true;
            return;
        }

        PublishPresence(true);
    }

    private void PublishPresence(bool enabled)
    {
        if (!PhotonNetwork.InRoom || (enabled && !IsPresenceRoomAllowed()))
            return;

        Player localPlayer = PhotonNetwork.LocalPlayer;
        if (localPlayer == null || (!enabled && !HasPresenceMarker(localPlayer)))
            return;

        presenceConfirmed = false;
        nextPresenceAttemptTime = enabled ? Time.unscaledTime + 5f : 0f;
        string value = enabled ? PresenceValue : DisabledPresenceValue;
        Hashtable properties = new Hashtable
        {
            [PresenceKey] = value,
            [LegacyPresenceKey] = value
        };
        localPlayer.SetCustomProperties(properties);

        if (Log != null)
            Log.LogInfo(enabled
                ? "Published current and legacy cosmetic presence markers."
                : "Disabled current and legacy cosmetic presence markers.");
    }

    private static bool IsPresenceRoomAllowed()
    {
        return IsInUtillaModdedLobby();
    }

    private static bool IsInUtillaModdedLobby()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null
            || PhotonNetwork.CurrentRoom.CustomProperties == null)
            return false;

        object gameMode;
        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("gameMode", out gameMode))
            return false;

        return gameMode is string
            && ((string)gameMode).StartsWith("MODDED_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPresenceMarker(Player player)
    {
        if (player == null || player.CustomProperties == null)
            return false;

        object value = player.CustomProperties[PresenceKey];
        if (value is string && (string)value == PresenceValue)
            return true;

        value = player.CustomProperties[LegacyPresenceKey];
        return value is string && (string)value == PresenceValue;
    }

    private void ApplyToRig(VRRig rig)
    {
        if (rig == null)
            return;

        RigState state;
        if (!states.TryGetValue(rig, out state))
        {
            state = new RigState();
            states.Add(rig, state);
        }

        if (Time.unscaledTime >= state.NextLookupTime)
        {
            state.NextLookupTime = Time.unscaledTime + LookupInterval;
            if (state.LeftShoulder == null)
                state.LeftShoulder = FindDescendant(rig.transform, "shoulder.L");
            if (state.RightShoulder == null)
                state.RightShoulder = FindDescendant(rig.transform, "shoulder.R");
            if (!state.VisualRigReady)
                TryCreateVisualHandRig(rig, state);
            else
            {
                RegisterHandAttachedVisuals(rig, state);
                if (state.BodyDockBindings.Count == 0)
                    RegisterBodyDockPositions(rig, state);
            }
        }

        if (state.LeftShoulder == null || state.RightShoulder == null)
        {
            if (!state.LoggedMissingShoulders)
            {
                if (Log != null)
                    Log.LogWarning("Shoulder transforms were not found on a rig; skipping that rig.");
                state.LoggedMissingShoulders = true;
            }
            return;
        }

        if (!state.HasOriginalShoulders)
        {
            state.LeftShoulderOriginal = state.LeftShoulder.localPosition;
            state.RightShoulderOriginal = state.RightShoulder.localPosition;
            state.HasOriginalShoulders = true;
        }

        float drop = ShoulderDrop != null ? Mathf.Clamp(ShoulderDrop.Value, 0f, 0.5f) : 0.35f;
        bool atComputer = (AlignHandsAtKeyboard == null || AlignHandsAtKeyboard.Value)
            && IsNearComputerInteraction(state, 0.30f, 1.35f);
        bool atWardrobe = (RestoreHandsAtWardrobe == null || RestoreHandsAtWardrobe.Value)
            && IsNearCosmeticWardrobe(rig);
        bool atInteractionStation = atComputer || atWardrobe;
        float appliedDrop = atInteractionStation ? 0f : drop;
        state.LeftShoulder.localPosition = new Vector3(state.LeftShoulderOriginal.x,
            state.LeftShoulderOriginal.y - appliedDrop, state.LeftShoulderOriginal.z);
        state.RightShoulder.localPosition = new Vector3(state.RightShoulderOriginal.x,
            state.RightShoulderOriginal.y - appliedDrop, state.RightShoulderOriginal.z);

        ApplyVisualHandOffset(rig, state, drop,
            HandRaise != null ? Mathf.Clamp(HandRaise.Value, 0.002f, 0.002f) : 0.0290f,
            HandTowardTorso != null ? Mathf.Clamp(HandTowardTorso.Value, 0.0465f, 0.0465f) : 0.08f,
            atInteractionStation);
        for (int i = 0; i < state.BodyDockBindings.Count; i++)
            state.BodyDockBindings[i].Apply();
    }

    private bool IsNearComputerInteraction(RigState state, float keyRange, float stationRange)
    {
        if (!state.VisualRigReady)
            return false;

        for (int i = 0; i < state.HandProxies.Count; i++)
        {
            HandProxy proxy = state.HandProxies[i];
            if (proxy.IsNearComputerKey(computerKeyColliders, keyRange)
                || proxy.IsNearComputerTerminal(computerTerminalTransforms, stationRange))
                return true;
        }
        return false;
    }

    private bool IsNearCosmeticWardrobe(VRRig rig)
    {
        if (rig == null)
            return false;

        RigContainer rigContainer = rig.GetComponentInParent<RigContainer>();
        Vector3 headPosition;
        if (rigContainer != null && rigContainer.HeadCollider != null)
            headPosition = rigContainer.HeadCollider.transform.position;
        else if (rig.head != null && rig.head.rigTarget != null)
            headPosition = rig.head.rigTarget.position;
        else
            return false;

        for (int i = 0; i < cosmeticWardrobeAnchors.Count; i++)
        {
            Transform anchor = cosmeticWardrobeAnchors[i];
            if (anchor != null && Vector3.Distance(headPosition, anchor.position) <= 2.0f)
                return true;
        }
        return false;
    }

    private bool TryCreateVisualHandRig(VRRig rig, RigState state)
    {
        Transform leftRoot = rig.transform.Find("rig/hand.L");
        Transform rightRoot = rig.transform.Find("rig/hand.R");
        SphereCollider leftMarker = FindSphereCollider(rig.transform,
            "rig/hand.L/palm.01.L/GameGrabbableL/ScannableHand");
        SphereCollider rightMarker = FindSphereCollider(rig.transform,
            "rig/hand.R/palm.01.R/GameGrabbableR/ScannableHand");
        if (leftRoot == null || rightRoot == null || leftMarker == null || rightMarker == null)
            return false;

        var leftMap = new Dictionary<Transform, Transform>();
        var rightMap = new Dictionary<Transform, Transform>();
        Transform leftProxyRoot = CloneTransformTree(leftRoot, leftRoot.parent, leftMap);
        Transform rightProxyRoot = CloneTransformTree(rightRoot, rightRoot.parent, rightMap);
        if (leftProxyRoot == null || rightProxyRoot == null)
        {
            RestoreVisualRig(rig, state);
            return false;
        }

        state.HandProxies.Add(new HandProxy(leftRoot, leftProxyRoot, leftMarker, leftMap));
        state.HandProxies.Add(new HandProxy(rightRoot, rightProxyRoot, rightMarker, rightMap));
        RegisterBodyDockPositions(rig, state);
        ReanchorHeldSnowballs(rig, state);
        RegisterHandAttachedVisuals(rig, state);

        var allMappings = new Dictionary<Transform, Transform>();
        foreach (KeyValuePair<Transform, Transform> pair in leftMap)
            allMappings[pair.Key] = pair.Value;
        foreach (KeyValuePair<Transform, Transform> pair in rightMap)
            allMappings[pair.Key] = pair.Value;

        int changedRenderers = 0;
        SkinnedMeshRenderer[] renderers = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            Transform[] originalBones = renderer.bones;
            if (originalBones == null || originalBones.Length == 0)
                continue;

            Transform[] proxyBones = (Transform[])originalBones.Clone();
            bool changed = false;
            for (int j = 0; j < originalBones.Length; j++)
            {
                Transform originalBone = originalBones[j];
                Transform proxyBone;
                if (originalBone != null && allMappings.TryGetValue(originalBone, out proxyBone))
                {
                    proxyBones[j] = proxyBone;
                    changed = true;
                }
            }

            Transform originalRootBone = renderer.rootBone;
            Transform proxyRootBone = null;
            bool rootChanged = originalRootBone != null
                && allMappings.TryGetValue(originalRootBone, out proxyRootBone);
            if (!changed && !rootChanged)
                continue;

            state.RendererBackups.Add(new RendererBoneBackup(renderer, originalBones, originalRootBone));
            renderer.bones = proxyBones;
            if (rootChanged)
                renderer.rootBone = proxyRootBone;
            changedRenderers++;
        }

        if (changedRenderers == 0)
        {
            if (Log != null)
                Log.LogWarning("No skinned renderer referenced this rig's hands; visual proxies were not applied.");
            RestoreVisualRig(rig, state);
            return false;
        }

        state.VisualRigReady = true;
        if (!state.LoggedVisualRig)
        {
            if (Log != null)
                Log.LogInfo("Created animated hand proxies and aligned hand grab, cosmetic, and projectile anchors; locomotion controller transforms are unchanged.");
            state.LoggedVisualRig = true;
        }
        return true;
    }

    private void RegisterHandAttachedVisuals(VRRig rig, RigState state)
    {
        Renderer[] renderers = rig.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is SkinnedMeshRenderer)
                continue;

            RegisterHandAttachment(state, renderer.transform);
        }

        MonoBehaviour[] behaviours = rig.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour.GetType().FullName
                != "GorillaTag.Cosmetics.ProjectileShooterCosmetic")
                continue;

            RegisterHandAttachment(state, behaviour.transform);
            FieldInfo shootFromField = behaviour.GetType().GetField("shootFromTransform",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (shootFromField != null)
                RegisterProjectileOrigin(state, behaviour, shootFromField);
        }
    }

    private void RegisterBodyDockPositions(VRRig rig, RigState state)
    {
        BodyDockPositions docks = rig != null ? rig.myBodyDockPositions : null;
        if (docks == null || state.HandProxies.Count < 2)
            return;

        Transform proxyLeft = state.HandProxies[0].GetOrCreateDockAnchor(
            docks.leftHandTransform, "Left");
        Transform proxyRight = state.HandProxies[1].GetOrCreateDockAnchor(
            docks.rightHandTransform, "Right");
        if (proxyLeft == null || proxyRight == null)
            return;

        state.BodyDockBindings.Add(new BodyDockPositionBinding(docks,
            docks.leftHandTransform, docks.rightHandTransform, proxyLeft, proxyRight));
        state.BodyDockBindings[state.BodyDockBindings.Count - 1].Apply();
        if (rig == VRRig.LocalRig && Log != null)
            Log.LogInfo("Mapped local holdable hand docks to the visible hand proxies.");
    }

    private static void RegisterHandAttachment(RigState state, Transform source)
    {
        if (source == null)
            return;

        for (int i = 0; i < state.HandProxies.Count; i++)
        {
            if (state.HandProxies[i].TryRegisterAuxiliaryAnchor(source))
                return;
        }
    }

    private static void RegisterProjectileOrigin(RigState state, MonoBehaviour owner, FieldInfo field)
    {
        if (owner == null || field == null)
            return;

        for (int i = 0; i < state.HandProxies.Count; i++)
        {
            if (state.HandProxies[i].TryRegisterProjectileOrigin(owner, field))
                return;
        }
    }

    private static Transform CloneTransformTree(Transform source, Transform parent,
        Dictionary<Transform, Transform> map)
    {
        if (source == null)
            return null;

        var cloneObject = new GameObject("__LegsModVisual_" + source.name);
        cloneObject.hideFlags = HideFlags.HideInHierarchy;
        Transform clone = cloneObject.transform;
        clone.SetParent(parent, false);
        clone.localPosition = source.localPosition;
        clone.localRotation = source.localRotation;
        clone.localScale = source.localScale;
        map[source] = clone;

        for (int i = 0; i < source.childCount; i++)
            CloneTransformTree(source.GetChild(i), clone, map);

        return clone;
    }

    private void ApplyVisualHandOffset(VRRig rig, RigState state, float drop,
        float raise, float towardTorso, bool alignAtComputer)
    {
        if (!state.VisualRigReady)
            return;

        Vector3 offset = rig.transform.up * (raise - drop)
            - rig.transform.forward * towardTorso;
        for (int i = 0; i < state.HandProxies.Count; i++)
        {
            HandProxy proxy = state.HandProxies[i];
            proxy.RestoreAuxiliaryAnchors();
            proxy.RestoreAttachmentAnchorPose();
            proxy.SyncPose();
            if (alignAtComputer)
                proxy.ProxyRoot.position = proxy.SourceRoot.position;
            else
            {
                Vector3 markerOffset = proxy.SourceRoot.TransformVector(proxy.RootToMarkerLocal);
                proxy.ProxyRoot.position = proxy.SourceRoot.position + markerOffset * 0.5f + offset;
            }
            proxy.ApplyProjectileOrigins();
            proxy.FollowAuxiliaryAnchors();
            proxy.MoveAttachmentAnchorToVisualHand();
        }
    }

    private void RefreshComputerKeyColliders()
    {
        if (Time.unscaledTime < nextComputerKeyLookupTime)
            return;

        nextComputerKeyLookupTime = Time.unscaledTime + 1.5f;
        computerKeyColliders.Clear();
        GorillaNetworking.GorillaKeyboardButton[] buttons =
            UnityEngine.Object.FindObjectsByType<GorillaNetworking.GorillaKeyboardButton>(FindObjectsSortMode.None);
        for (int i = 0; i < buttons.Length; i++)
        {
            GorillaNetworking.GorillaKeyboardButton button = buttons[i];
            if (button == null)
                continue;

            Collider keyCollider = button.GetComponent<Collider>();
            if (keyCollider == null)
                keyCollider = button.GetComponentInChildren<Collider>();
            if (keyCollider == null)
                keyCollider = button.GetComponentInParent<Collider>();
            if (keyCollider != null && !computerKeyColliders.Contains(keyCollider))
                computerKeyColliders.Add(keyCollider);
        }

        computerTerminalTransforms.Clear();
        GorillaComputerTerminal[] terminals =
            UnityEngine.Object.FindObjectsByType<GorillaComputerTerminal>(FindObjectsSortMode.None);
        for (int i = 0; i < terminals.Length; i++)
        {
            GorillaComputerTerminal terminal = terminals[i];
            if (terminal != null && terminal.transform != null)
                computerTerminalTransforms.Add(terminal.transform);
        }

        cosmeticWardrobeAnchors.Clear();
        CosmeticWardrobe[] wardrobes =
            UnityEngine.Object.FindObjectsByType<CosmeticWardrobe>(FindObjectsSortMode.None);
        for (int i = 0; i < wardrobes.Length; i++)
        {
            CosmeticWardrobe wardrobe = wardrobes[i];
            if (wardrobe == null)
                continue;

            if (!cosmeticWardrobeAnchors.Contains(wardrobe.transform))
                cosmeticWardrobeAnchors.Add(wardrobe.transform);

            GorillaPressableButton[] wardrobeButtons = wardrobe.GetComponentsInChildren<GorillaPressableButton>(true);
            for (int j = 0; j < wardrobeButtons.Length; j++)
            {
                GorillaPressableButton button = wardrobeButtons[j];
                if (button != null && button.transform != null
                    && !cosmeticWardrobeAnchors.Contains(button.transform))
                    cosmeticWardrobeAnchors.Add(button.transform);
            }
        }

        string targetCounts = computerKeyColliders.Count + "/" + computerTerminalTransforms.Count
            + "/" + cosmeticWardrobeAnchors.Count;
        if (targetCounts != lastLoggedInteractionTargetCounts)
        {
            lastLoggedInteractionTargetCounts = targetCounts;
            if (Log != null)
                Log.LogInfo("Interaction scan: computer key colliders=" + computerKeyColliders.Count
                    + ", terminals=" + computerTerminalTransforms.Count
                    + ", owned-cosmetics wardrobe anchors=" + cosmeticWardrobeAnchors.Count + ".");
        }
    }

    private void RestoreVisualRig(VRRig rig, RigState state)
    {
        for (int i = 0; i < state.BodyDockBindings.Count; i++)
            state.BodyDockBindings[i].Restore();
        ReanchorHeldSnowballs(rig, state);
        state.BodyDockBindings.Clear();

        for (int i = 0; i < state.HeldThrowableBindings.Count; i++)
        {
            HeldThrowableBinding binding = state.HeldThrowableBindings[i];
            if (binding == null)
                continue;

            binding.Deactivate();
            if (binding.Throwable != null && binding.Throwable.isHeld
                && !dormantThrowableBindings.Contains(binding))
                dormantThrowableBindings.Add(binding);
        }
        state.HeldThrowableBindings.Clear();

        for (int i = 0; i < state.RendererBackups.Count; i++)
        {
            RendererBoneBackup backup = state.RendererBackups[i];
            if (backup.Renderer == null)
                continue;
            backup.Renderer.bones = backup.OriginalBones;
            backup.Renderer.rootBone = backup.OriginalRootBone;
        }
        state.RendererBackups.Clear();

        for (int i = 0; i < state.HandProxies.Count; i++)
        {
            HandProxy proxy = state.HandProxies[i];
            proxy.RestoreProjectileOrigins();
            proxy.RestoreAuxiliaryAnchors();
            proxy.RestoreAttachmentAnchorPose();
            if (proxy.ProxyRoot != null)
                UnityEngine.Object.Destroy(proxy.ProxyRoot.gameObject);
        }
        state.HandProxies.Clear();
        state.VisualRigReady = false;
    }

    private void RestoreRig(VRRig rig)
    {
        if (rig == null)
            return;

        RigState state;
        if (!states.TryGetValue(rig, out state))
            return;

        if (state.HasOriginalShoulders)
        {
            if (state.LeftShoulder != null)
                state.LeftShoulder.localPosition = state.LeftShoulderOriginal;
            if (state.RightShoulder != null)
                state.RightShoulder.localPosition = state.RightShoulderOriginal;
        }
        RestoreVisualRig(rig, state);
        states.Remove(rig);
    }

    private void RestoreNonLocalRigs()
    {
        VRRig localRig = VRRig.LocalRig;
        staleRigs.Clear();
        foreach (KeyValuePair<VRRig, RigState> pair in states)
        {
            VRRig rig = pair.Key;
            if (rig != null && rig != localRig)
                staleRigs.Add(rig);
        }
        for (int i = 0; i < staleRigs.Count; i++)
            RestoreRig(staleRigs[i]);
    }

    private void RestoreAllRigs()
    {
        RestoreHeldEquipmentBindings(false, false);
        staleRigs.Clear();
        foreach (KeyValuePair<VRRig, RigState> pair in states)
            staleRigs.Add(pair.Key);

        for (int i = 0; i < staleRigs.Count; i++)
            RestoreRig(staleRigs[i]);
    }

    private void RemoveDestroyedRigStates()
    {
        staleRigs.Clear();
        foreach (KeyValuePair<VRRig, RigState> pair in states)
        {
            if (pair.Key == null)
            {
                RestoreVisualRig(null, pair.Value);
                staleRigs.Add(pair.Key);
            }
        }
        for (int i = 0; i < staleRigs.Count; i++)
            states.Remove(staleRigs[i]);
    }

    private void UpdateHeldEquipmentBindings()
    {
        VRRig localRig = VRRig.LocalRig;
        if (localRig == null)
        {
            RestoreHeldEquipmentBindings(false, false);
            equipmentInteractor = null;
            return;
        }

        if (equipmentInteractor == null || !equipmentInteractor.gameObject.activeInHierarchy
            || Time.unscaledTime >= nextEquipmentInteractorLookupTime)
        {
            nextEquipmentInteractorLookupTime = Time.unscaledTime + 1f;
            equipmentInteractor = FindLocalEquipmentInteractor(localRig);
            if (equipmentInteractor != null && !loggedEquipmentInteractorFound && Log != null)
            {
                Log.LogInfo("Found local EquipmentInteractor for held-item alignment.");
                loggedEquipmentInteractorFound = true;
            }
            if (equipmentInteractor == null && !loggedMissingEquipmentInteractor && Log != null)
            {
                Log.LogInfo("Local EquipmentInteractor is not ready yet; held-item alignment will retry.");
                loggedMissingEquipmentInteractor = true;
            }
        }

        if (equipmentInteractor == null)
        {
            for (int i = 0; i < heldEquipmentBindings.Count; i++)
                heldEquipmentBindings[i].Apply();
            return;
        }

        RigState localState;
        if (!states.TryGetValue(localRig, out localState) || !localState.VisualRigReady
            || localState.HandProxies.Count < 2)
            return;

        var heldNow = new HashSet<IHoldableObject>();
        UpdateHeldEquipmentSlot(equipmentInteractor.leftHandHeldEquipment,
            equipmentInteractor.leftHand, localState.HandProxies[0], true, heldNow);
        UpdateHeldEquipmentSlot(equipmentInteractor.rightHandHeldEquipment,
            equipmentInteractor.rightHand, localState.HandProxies[1], false, heldNow);

        for (int i = heldEquipmentBindings.Count - 1; i >= 0; i--)
        {
            HeldEquipmentBinding binding = heldEquipmentBindings[i];
            if (binding == null || binding.Holdable == null || !heldNow.Contains(binding.Holdable))
            {
                if (binding != null)
                    binding.Restore(true);
                heldEquipmentBindings.RemoveAt(i);
            }
        }
    }

    private static EquipmentInteractor FindLocalEquipmentInteractor(VRRig localRig)
    {
        if (localRig == null)
            return null;

        EquipmentInteractor localChild = localRig.GetComponentInChildren<EquipmentInteractor>(true);
        if (localChild == null)
            localChild = localRig.GetComponent<EquipmentInteractor>();
        if (localChild != null)
            return localChild;

        EquipmentInteractor[] candidates = UnityEngine.Object.FindObjectsByType<EquipmentInteractor>(
            FindObjectsSortMode.None);
        if (candidates == null || candidates.Length == 0)
            return null;
        if (candidates.Length == 1)
            return candidates[0];

        Transform localLeft = localRig.transform.Find("rig/hand.L");
        Transform localRight = localRig.transform.Find("rig/hand.R");
        float bestScore = float.MaxValue;
        EquipmentInteractor best = null;
        for (int i = 0; i < candidates.Length; i++)
        {
            EquipmentInteractor candidate = candidates[i];
            if (candidate == null || !candidate.gameObject.activeInHierarchy
                || candidate.leftHand == null || candidate.rightHand == null)
                continue;

            float score = 0f;
            int comparisons = 0;
            if (localLeft != null)
            {
                score += Vector3.Distance(localLeft.position, candidate.leftHand.transform.position);
                comparisons++;
            }
            if (localRight != null)
            {
                score += Vector3.Distance(localRight.position, candidate.rightHand.transform.position);
                comparisons++;
            }
            if (comparisons == 0)
                continue;

            score /= comparisons;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return bestScore <= 5f ? best : null;
    }

    private void UpdateHeldEquipmentSlot(IHoldableObject holdable, GameObject handObject,
        HandProxy proxy, bool leftHand, HashSet<IHoldableObject> heldNow)
    {
        if (holdable == null)
            return;

        heldNow.Add(holdable);
        if (holdable is GorillaThrowable || UsesBodyDock(holdable))
            return;

        Component component = holdable as Component;
        if (component == null || component.transform == null)
            return;

        Transform equipmentHand = handObject != null ? handObject.transform : proxy.SourceRoot;
        Transform currentAnchor = equipmentHand;
        Transform visualAnchor = proxy.GetMappedTarget(equipmentHand);
        Transform restoreAnchor = equipmentHand;

        if (visualAnchor == null)
            visualAnchor = proxy.ProxyRoot;
        if (currentAnchor == null)
            currentAnchor = equipmentHand;
        if (restoreAnchor == null)
            restoreAnchor = equipmentHand;
        if (currentAnchor == null || visualAnchor == null || restoreAnchor == null)
            return;

        HeldEquipmentBinding existing = FindHeldEquipmentBinding(holdable);
        if (existing == null)
        {
            existing = new HeldEquipmentBinding(holdable, component.transform,
                currentAnchor, visualAnchor, restoreAnchor);
            heldEquipmentBindings.Add(existing);
            if (Log != null)
                Log.LogInfo("Mapped held item " + component.GetType().Name
                    + " to the visible " + (leftHand ? "left" : "right") + " hand.");
        }
        else
            existing.SetAnchors(currentAnchor, visualAnchor, restoreAnchor);

        existing.Apply();
    }

    private static bool UsesBodyDock(IHoldableObject holdable)
    {
        if (holdable == null)
            return false;

        for (Type type = holdable.GetType(); type != null; type = type.BaseType)
        {
            if (type.Name == "TransferrableObject" || type.Name == "SnowballThrowable")
                return true;
        }
        return false;
    }

    private HeldEquipmentBinding FindHeldEquipmentBinding(IHoldableObject holdable)
    {
        if (holdable == null)
            return null;

        for (int i = 0; i < heldEquipmentBindings.Count; i++)
        {
            HeldEquipmentBinding binding = heldEquipmentBindings[i];
            if (binding != null && binding.Holdable == holdable)
                return binding;
        }
        return null;
    }

    internal void PrepareEquipmentRelease(EquipmentInteractor interactor, IHoldableObject holdable)
    {
        if (interactor != null)
            equipmentInteractor = interactor;

        HeldEquipmentBinding binding = FindHeldEquipmentBinding(holdable);
        if (binding != null)
        {
            if (legsEnabled)
                binding.Apply();
            else
                binding.Restore(false);
        }

        GorillaThrowable throwable = holdable as GorillaThrowable;
        if (throwable != null)
            PrepareThrowableThrow(throwable);

        SnowballThrowable snowball = holdable as SnowballThrowable;
        if (snowball != null)
            ReanchorSnowballToItsCurrentDock(snowball);
    }

    internal void FinishEquipmentRelease(IHoldableObject holdable)
    {
        HeldEquipmentBinding binding = FindHeldEquipmentBinding(holdable);
        if (binding != null)
        {
            if (legsEnabled)
                binding.Restore(true);
            heldEquipmentBindings.Remove(binding);
        }

        GorillaThrowable throwable = holdable as GorillaThrowable;
        if (throwable != null)
            FinishThrowableThrow(throwable);
    }

    private void RestoreHeldEquipmentBindings(bool preserveLastPose, bool clearBindings)
    {
        for (int i = 0; i < heldEquipmentBindings.Count; i++)
        {
            HeldEquipmentBinding binding = heldEquipmentBindings[i];
            if (binding != null)
            {
                binding.Restore(preserveLastPose);
                if (!preserveLastPose)
                    binding.Deactivate();
            }
        }
        if (clearBindings)
            heldEquipmentBindings.Clear();
    }

    private void RemoveReleasedHeldEquipmentBindings()
    {
        if (equipmentInteractor == null)
            return;

        IHoldableObject left = equipmentInteractor.leftHandHeldEquipment;
        IHoldableObject right = equipmentInteractor.rightHandHeldEquipment;
        for (int i = heldEquipmentBindings.Count - 1; i >= 0; i--)
        {
            HeldEquipmentBinding binding = heldEquipmentBindings[i];
            if (binding == null || (binding.Holdable != left && binding.Holdable != right))
                heldEquipmentBindings.RemoveAt(i);
        }
    }

    private void UpdateHeldThrowableBindings()
    {
        for (int i = dormantThrowableBindings.Count - 1; i >= 0; i--)
        {
            HeldThrowableBinding dormant = dormantThrowableBindings[i];
            if (dormant == null || dormant.Throwable == null || !dormant.Throwable.isHeld)
            {
                if (dormant != null)
                    dormant.RestoreReferences();
                dormantThrowableBindings.RemoveAt(i);
            }
        }

        foreach (KeyValuePair<VRRig, RigState> pair in states)
        {
            List<HeldThrowableBinding> bindings = pair.Value.HeldThrowableBindings;
            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                HeldThrowableBinding binding = bindings[i];
                if (binding == null || binding.Throwable == null || !binding.Throwable.isHeld)
                {
                    if (binding != null)
                        binding.Restore(true);
                    bindings.RemoveAt(i);
                }
                else
                    binding.Apply();
            }
        }

        if (Time.unscaledTime < nextHeldThrowableLookupTime)
            return;

        nextHeldThrowableLookupTime = Time.unscaledTime + HeldThrowableLookupInterval;
        GorillaThrowable[] throwables = UnityEngine.Object.FindObjectsByType<GorillaThrowable>(
            FindObjectsSortMode.None);
        for (int i = 0; i < throwables.Length; i++)
        {
            GorillaThrowable throwable = throwables[i];
            if (throwable == null || !throwable.isHeld)
                continue;

            HeldThrowableBinding existing = FindHeldThrowableBinding(throwable);
            Transform sourceGrab = existing != null
                ? existing.OriginalGrabTransform : throwable.grabbingTransform;
            Transform sourceFollow = existing != null
                ? existing.OriginalFollowTransform : ReadThrowableFollowTransform(throwable);
            RigState ownerState;
            Transform proxyTarget;
            if (!TryResolveHeldThrowableTarget(throwable.transform.position,
                sourceGrab, sourceFollow, out ownerState, out proxyTarget))
            {
                if (existing != null)
                    existing.Apply();
                continue;
            }

            if (existing != null)
            {
                existing.SetTarget(proxyTarget);
                dormantThrowableBindings.Remove(existing);
                if (!ownerState.HeldThrowableBindings.Contains(existing))
                    ownerState.HeldThrowableBindings.Add(existing);
                continue;
            }

            var binding = new HeldThrowableBinding(throwable, proxyTarget,
                sourceGrab, sourceFollow, ThrowableFollowTransformField);
            ownerState.HeldThrowableBindings.Add(binding);
            binding.Apply();
            if (Log != null)
                Log.LogInfo("Aligned a held throwable with its visible hand.");
        }
    }

    internal void BindThrowableImmediately(GorillaThrowable throwable, Transform grabArgument)
    {
        if (!legsEnabled || throwable == null)
            return;

        if (FindHeldEquipmentBinding(throwable as IHoldableObject) != null)
            return;

        VRRig localRig = VRRig.LocalRig;
        if (localRig != null)
            ApplyToRig(localRig);

        HeldThrowableBinding existing = FindHeldThrowableBinding(throwable);
        Transform originalGrab = existing != null
            ? existing.OriginalGrabTransform : throwable.grabbingTransform;
        Transform originalFollow = existing != null
            ? existing.OriginalFollowTransform : ReadThrowableFollowTransform(throwable);
        Transform sourceGrab = grabArgument != null ? grabArgument : originalGrab;
        RigState ownerState;
        Transform proxyTarget;
        if (!TryResolveHeldThrowableTarget(throwable.transform.position,
            sourceGrab, originalFollow, out ownerState, out proxyTarget))
            return;

        if (existing != null)
        {
            existing.SetTarget(proxyTarget);
            dormantThrowableBindings.Remove(existing);
            if (!ownerState.HeldThrowableBindings.Contains(existing))
                ownerState.HeldThrowableBindings.Add(existing);
            return;
        }

        var binding = new HeldThrowableBinding(throwable, proxyTarget,
            originalGrab, originalFollow, ThrowableFollowTransformField);
        ownerState.HeldThrowableBindings.Add(binding);
        binding.Apply();
        if (Log != null)
            Log.LogInfo("Bound a newly grabbed throwable to its visible hand.");
    }

    internal void PrepareThrowableThrow(GorillaThrowable throwable)
    {
        HeldEquipmentBinding equipmentBinding = FindHeldEquipmentBinding(
            throwable as IHoldableObject);
        if (equipmentBinding != null)
        {
            equipmentBinding.Apply();
            return;
        }

        HeldThrowableBinding binding = FindHeldThrowableBinding(throwable);
        if (binding != null && binding.IsActive)
            binding.Apply();
        else if (throwable != null && legsEnabled)
            BindThrowableImmediately(throwable, throwable.grabbingTransform);
    }

    internal void FinishThrowableThrow(GorillaThrowable throwable)
    {
        HeldThrowableBinding binding = FindHeldThrowableBinding(throwable);
        if (binding == null)
            return;

        if (binding.IsActive)
            binding.Restore(true);
        else
            binding.RestoreReferences();
        foreach (KeyValuePair<VRRig, RigState> pair in states)
            pair.Value.HeldThrowableBindings.Remove(binding);
        dormantThrowableBindings.Remove(binding);
    }

    private HeldThrowableBinding FindHeldThrowableBinding(GorillaThrowable throwable)
    {
        foreach (KeyValuePair<VRRig, RigState> pair in states)
        {
            List<HeldThrowableBinding> bindings = pair.Value.HeldThrowableBindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i] != null && bindings[i].Throwable == throwable)
                    return bindings[i];
            }
        }
        for (int i = 0; i < dormantThrowableBindings.Count; i++)
        {
            HeldThrowableBinding binding = dormantThrowableBindings[i];
            if (binding != null && binding.Throwable == throwable)
                return binding;
        }
        return null;
    }

    private bool TryResolveHeldThrowableTarget(Vector3 itemPosition,
        Transform grabbingTransform, Transform followTransform,
        out RigState ownerState, out Transform proxyTarget)
    {
        ownerState = null;
        proxyTarget = null;
        float bestScore = HeldThrowableHandMatchRange;

        foreach (KeyValuePair<VRRig, RigState> pair in states)
        {
            RigState state = pair.Value;
            if (pair.Key == null || !state.VisualRigReady)
                continue;

            for (int i = 0; i < state.HandProxies.Count; i++)
            {
                Transform candidate;
                float score;
                if (!state.HandProxies[i].TryResolveHeldTarget(grabbingTransform,
                    followTransform, itemPosition, out candidate, out score))
                    continue;

                if (score >= bestScore)
                    continue;

                bestScore = score;
                ownerState = state;
                proxyTarget = candidate;
                if (score < 0f)
                    return true;
            }
        }

        return ownerState != null && proxyTarget != null;
    }

    private static Transform ReadThrowableFollowTransform(GorillaThrowable throwable)
    {
        if (throwable == null || ThrowableFollowTransformField == null)
            return null;

        try
        {
            return ThrowableFollowTransformField.GetValue(throwable) as Transform;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ReanchorHeldSnowballs(VRRig rig, RigState state)
    {
        if (rig == null || state == null)
            return;
        if (SnowballAnchorToHandMethod == null)
        {
            if (!loggedMissingSnowballAnchorMethod && Log != null)
            {
                Log.LogWarning("SnowballThrowable.AnchorToHand() was not found; held snowballs cannot be reparented on toggle.");
                loggedMissingSnowballAnchorMethod = true;
            }
            return;
        }

        SnowballThrowable[] snowballs = UnityEngine.Object.FindObjectsByType<SnowballThrowable>(
            FindObjectsSortMode.None);
        for (int i = 0; i < snowballs.Length; i++)
        {
            SnowballThrowable snowball = snowballs[i];
            if (snowball == null || !IsSnowballHeld(snowball)
                || !SnowballBelongsToRig(snowball, rig, state))
                continue;

            ReanchorSnowballToItsCurrentDock(snowball);
        }
    }

    private bool IsSnowballHeld(SnowballThrowable snowball)
    {
        IHoldableObject holdable = snowball as IHoldableObject;
        if (holdable != null && equipmentInteractor != null
            && (equipmentInteractor.leftHandHeldEquipment == holdable
                || equipmentInteractor.rightHandHeldEquipment == holdable))
            return true;

        object heldValue = ReadNamedMember(snowball, "isHeld", "IsHeld", "isGrabbed", "held");
        if (heldValue is bool)
            return (bool)heldValue;

        Transform anchor = SnowballAnchorField != null
            ? SnowballAnchorField.GetValue(snowball) as Transform : null;
        if (anchor == null)
            anchor = snowball.transform;

        foreach (KeyValuePair<VRRig, RigState> pair in states)
        {
            RigState state = pair.Value;
            for (int i = 0; i < state.HandProxies.Count; i++)
            {
                Transform proxyRoot = state.HandProxies[i].ProxyRoot;
                if (proxyRoot != null && (anchor == proxyRoot || anchor.IsChildOf(proxyRoot)))
                    return true;
            }
            for (int i = 0; i < state.BodyDockBindings.Count; i++)
            {
                BodyDockPositionBinding docks = state.BodyDockBindings[i];
                Transform[] candidates =
                {
                    docks.GetOriginalHand(true), docks.GetOriginalHand(false),
                    docks.GetProxyHand(true), docks.GetProxyHand(false)
                };
                for (int j = 0; j < candidates.Length; j++)
                {
                    Transform candidate = candidates[j];
                    if (candidate != null && (anchor == candidate || anchor.IsChildOf(candidate)))
                        return true;
                }
            }
        }
        return false;
    }

    private static bool SnowballBelongsToRig(SnowballThrowable snowball, VRRig rig,
        RigState state)
    {
        object owner = SnowballTargetRigField != null
            ? SnowballTargetRigField.GetValue(snowball)
            : (SnowballTargetRigProperty != null ? SnowballTargetRigProperty.GetValue(snowball, null) : null);
        if (ReferenceEquals(owner, rig))
            return true;

        Component ownerComponent = owner as Component;
        if (ownerComponent != null && ownerComponent.GetComponentInParent<VRRig>() == rig)
            return true;

        Transform anchor = SnowballAnchorField != null
            ? SnowballAnchorField.GetValue(snowball) as Transform : null;
        if (anchor == null)
            anchor = snowball.transform;

        for (int i = 0; i < state.HandProxies.Count; i++)
        {
            HandProxy proxy = state.HandProxies[i];
            if (proxy.SourceRoot != null
                && (anchor == proxy.SourceRoot || anchor.IsChildOf(proxy.SourceRoot)))
                return true;
            if (proxy.ProxyRoot != null
                && (anchor == proxy.ProxyRoot || anchor.IsChildOf(proxy.ProxyRoot)))
                return true;
        }

        for (int i = 0; i < state.BodyDockBindings.Count; i++)
        {
            BodyDockPositionBinding docks = state.BodyDockBindings[i];
            Transform[] candidates =
            {
                docks.GetOriginalHand(true), docks.GetOriginalHand(false),
                docks.GetProxyHand(true), docks.GetProxyHand(false)
            };
            for (int j = 0; j < candidates.Length; j++)
            {
                Transform candidate = candidates[j];
                if (candidate != null && (anchor == candidate || anchor.IsChildOf(candidate)))
                    return true;
            }
        }
        return false;
    }

    private void ReanchorSnowballToItsCurrentDock(SnowballThrowable snowball)
    {
        if (snowball == null || SnowballAnchorToHandMethod == null)
            return;

        try
        {
            SnowballAnchorToHandMethod.Invoke(snowball, null);
            if (Log != null)
                Log.LogInfo("Re-anchored a held SnowballThrowable to its current hand dock.");
        }
        catch (Exception exception)
        {
            if (Log != null)
                Log.LogWarning("Could not restore a held snowball's hand anchor: "
                    + (exception.InnerException != null
                        ? exception.InnerException.Message : exception.Message));
        }
    }

    internal void ApplyHeldSnowballVisualOffset(SnowballThrowable snowball)
    {
        if (!legsEnabled || snowball == null || snowball.transform == null)
            return;

        VRRig localRig = VRRig.LocalRig;
        if (localRig == null)
            return;

        object owner = SnowballTargetRigField != null
            ? SnowballTargetRigField.GetValue(snowball)
            : (SnowballTargetRigProperty != null
                ? SnowballTargetRigProperty.GetValue(snowball, null) : null);
        if (!ReferenceEquals(owner, localRig))
            return;

        // Keep the grab dock untouched; only nudge the projectile's own visual/throw origin.
        snowball.transform.localPosition += HeldProjectileVisualOffset;
    }

    private static object ReadNamedMember(object instance, params string[] names)
    {
        if (instance == null || names == null)
            return null;

        Type type = instance.GetType();
        for (int i = 0; i < names.Length; i++)
        {
            FieldInfo field = FindInstanceField(type, names[i]);
            if (field != null)
                return field.GetValue(instance);
            PropertyInfo property = FindInstanceProperty(type, names[i]);
            if (property != null)
                return property.GetValue(instance, null);
        }
        return null;
    }

    private static MethodInfo FindInstanceMethod(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (Type current = type; current != null; current = current.BaseType)
        {
            MethodInfo method = current.GetMethod(name, flags, null, Type.EmptyTypes, null);
            if (method != null)
                return method;
        }
        return null;
    }

    private static FieldInfo FindInstanceField(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
    }

    private static PropertyInfo FindInstanceProperty(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (Type current = type; current != null; current = current.BaseType)
        {
            PropertyInfo property = current.GetProperty(name, flags | BindingFlags.DeclaredOnly);
            if (property != null)
                return property;
        }
        return null;
    }

    private static SphereCollider FindSphereCollider(Transform root, string relativePath)
    {
        Transform target = root != null ? root.Find(relativePath) : null;
        return target != null ? target.GetComponent<SphereCollider>() : null;
    }

    private static Transform FindDescendant(Transform root, string exactName)
    {
        if (root == null)
            return null;
        if (root.name == exactName)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDescendant(root.GetChild(i), exactName);
            if (found != null)
                return found;
        }
        return null;
    }

    private void OnDestroy()
    {
        RestoreHeldEquipmentBindings(false, true);
        foreach (KeyValuePair<VRRig, RigState> pair in states)
        {
            VRRig rig = pair.Key;
            RigState state = pair.Value;
            if (rig != null && state.HasOriginalShoulders)
            {
                if (state.LeftShoulder != null)
                    state.LeftShoulder.localPosition = state.LeftShoulderOriginal;
                if (state.RightShoulder != null)
                    state.RightShoulder.localPosition = state.RightShoulderOriginal;
            }
            RestoreVisualRig(rig, state);
        }
        states.Clear();
        for (int i = 0; i < dormantThrowableBindings.Count; i++)
        {
            HeldThrowableBinding binding = dormantThrowableBindings[i];
            if (binding != null)
                binding.Restore(false);
        }
        dormantThrowableBindings.Clear();
    }

    private sealed class HandProxy
    {
        public readonly Transform SourceRoot;
        public readonly Transform ProxyRoot;
        public readonly Vector3 RootToMarkerLocal;
        private readonly SphereCollider marker;
        private readonly Dictionary<Transform, Transform> mapping;
        private readonly Transform attachmentAnchor;
        private readonly Transform proxyAttachmentAnchor;
        private readonly Vector3 attachmentAnchorOriginalLocalPosition;
        private readonly Quaternion attachmentAnchorOriginalLocalRotation;
        private readonly Vector3 attachmentAnchorOriginalLocalScale;
        private readonly Vector3 markerCenterInAttachmentAnchorLocal;
        private readonly List<TransformPoseBinding> auxiliaryAnchors =
            new List<TransformPoseBinding>();
        private readonly HashSet<Transform> auxiliaryAnchorSources =
            new HashSet<Transform>();
        private readonly List<ProjectileOriginBinding> projectileOrigins =
            new List<ProjectileOriginBinding>();
        private readonly HashSet<MonoBehaviour> projectileOriginOwners =
            new HashSet<MonoBehaviour>();
        private readonly List<DockAnchorPoseBinding> dockAnchors =
            new List<DockAnchorPoseBinding>();
        private readonly HashSet<Transform> dockAnchorSources =
            new HashSet<Transform>();

        public HandProxy(Transform sourceRoot, Transform proxyRoot, SphereCollider marker,
            Dictionary<Transform, Transform> mapping)
        {
            SourceRoot = sourceRoot;
            ProxyRoot = proxyRoot;
            this.marker = marker;
            RootToMarkerLocal = sourceRoot.InverseTransformVector(marker.bounds.center - sourceRoot.position);
            this.mapping = mapping;

            Transform markerTransform = marker != null ? marker.transform : null;
            attachmentAnchor = markerTransform != null && markerTransform.parent != null
                ? markerTransform.parent : markerTransform;
            Transform clonedAnchor = null;
            if (attachmentAnchor != null)
                mapping.TryGetValue(attachmentAnchor, out clonedAnchor);
            proxyAttachmentAnchor = clonedAnchor;

            if (attachmentAnchor != null)
            {
                attachmentAnchorOriginalLocalPosition = attachmentAnchor.localPosition;
                attachmentAnchorOriginalLocalRotation = attachmentAnchor.localRotation;
                attachmentAnchorOriginalLocalScale = attachmentAnchor.localScale;
                if (marker != null)
                    markerCenterInAttachmentAnchorLocal =
                        attachmentAnchor.InverseTransformPoint(marker.bounds.center);
            }
        }

        private Vector3 GetOriginalMarkerCenter()
        {
            if (attachmentAnchor != null && marker != null)
            {
                Vector3 originalLocalCenter = attachmentAnchorOriginalLocalPosition
                    + attachmentAnchorOriginalLocalRotation
                    * Vector3.Scale(attachmentAnchorOriginalLocalScale,
                        markerCenterInAttachmentAnchorLocal);
                return attachmentAnchor.parent != null
                    ? attachmentAnchor.parent.TransformPoint(originalLocalCenter)
                    : originalLocalCenter;
            }

            return marker != null ? marker.bounds.center : Vector3.zero;
        }

        public void RestoreAttachmentAnchorPose()
        {
            if (attachmentAnchor == null)
                return;

            attachmentAnchor.localPosition = attachmentAnchorOriginalLocalPosition;
            attachmentAnchor.localRotation = attachmentAnchorOriginalLocalRotation;
            attachmentAnchor.localScale = attachmentAnchorOriginalLocalScale;
        }

        public void MoveAttachmentAnchorToVisualHand()
        {
            if (attachmentAnchor == null || proxyAttachmentAnchor == null)
                return;

            attachmentAnchor.position = proxyAttachmentAnchor.position;
            attachmentAnchor.rotation = proxyAttachmentAnchor.rotation;
        }

        public bool TryRegisterAuxiliaryAnchor(Transform source)
        {
            if (source == null || source == SourceRoot)
                return false;

            Transform proxy;
            if (!mapping.TryGetValue(source, out proxy))
                return false;

            if (source == attachmentAnchor
                || (attachmentAnchor != null && (source.IsChildOf(attachmentAnchor)
                    || attachmentAnchor.IsChildOf(source))))
                return true;

            if (auxiliaryAnchorSources.Add(source))
                auxiliaryAnchors.Add(new TransformPoseBinding(source, proxy));
            return true;
        }

        public Transform GetOrCreateDockAnchor(Transform source, string handName)
        {
            if (source == null || SourceRoot == null || ProxyRoot == null)
                return ProxyRoot;

            Transform mapped;
            if (mapping.TryGetValue(source, out mapped))
                return mapped;

            if (dockAnchorSources.Add(source))
            {
                var anchorObject = new GameObject("__LegsModDock_" + handName);
                anchorObject.hideFlags = HideFlags.HideInHierarchy;
                Transform anchor = anchorObject.transform;
                anchor.SetParent(ProxyRoot, false);
                dockAnchors.Add(new DockAnchorPoseBinding(SourceRoot, source, anchor));
                dockAnchors[dockAnchors.Count - 1].Sync();
            }

            for (int i = 0; i < dockAnchors.Count; i++)
            {
                if (dockAnchors[i].Source == source)
                    return dockAnchors[i].Proxy;
            }

            return ProxyRoot;
        }

        public bool TryResolveHeldTarget(Transform grabbingTransform,
            Transform followTransform, Vector3 itemPosition,
            out Transform proxyTarget, out float score)
        {
            proxyTarget = null;
            score = float.MaxValue;
            if (SourceRoot == null || ProxyRoot == null)
                return false;

            if (TryGetMappedTarget(grabbingTransform, out proxyTarget)
                || TryGetMappedTarget(followTransform, out proxyTarget))
            {
                score = -1f;
                return true;
            }

            if (grabbingTransform != null && grabbingTransform.IsChildOf(SourceRoot))
            {
                proxyTarget = ProxyRoot;
                score = 0f;
                return true;
            }
            if (followTransform != null && followTransform.IsChildOf(SourceRoot))
            {
                proxyTarget = ProxyRoot;
                score = 0f;
                return true;
            }

            float nearestDistance = float.MaxValue;
            if (grabbingTransform != null)
                nearestDistance = Mathf.Min(nearestDistance,
                    Vector3.Distance(grabbingTransform.position, SourceRoot.position));
            if (followTransform != null)
                nearestDistance = Mathf.Min(nearestDistance,
                    Vector3.Distance(followTransform.position, SourceRoot.position));
            if (grabbingTransform == null && followTransform == null)
                nearestDistance = Vector3.Distance(itemPosition, SourceRoot.position);

            if (nearestDistance > HeldThrowableHandMatchRange)
                return false;

            proxyTarget = ProxyRoot;
            score = nearestDistance;
            return true;
        }

        private bool TryGetMappedTarget(Transform source, out Transform target)
        {
            target = null;
            return source != null && mapping.TryGetValue(source, out target);
        }

        public Transform GetMappedTarget(Transform source)
        {
            Transform target;
            return TryGetMappedTarget(source, out target) ? target : ProxyRoot;
        }

        public bool TryRegisterProjectileOrigin(MonoBehaviour owner, FieldInfo field)
        {
            if (owner == null || field == null || !projectileOriginOwners.Add(owner))
                return false;

            Transform source = field.GetValue(owner) as Transform;
            Transform proxy;
            if (source == null || !mapping.TryGetValue(source, out proxy))
            {
                projectileOriginOwners.Remove(owner);
                return false;
            }

            projectileOrigins.Add(new ProjectileOriginBinding(owner, field, source, proxy));
            return true;
        }

        public void RestoreAuxiliaryAnchors()
        {
            for (int i = 0; i < auxiliaryAnchors.Count; i++)
                auxiliaryAnchors[i].Restore();
        }

        public void FollowAuxiliaryAnchors()
        {
            for (int i = 0; i < auxiliaryAnchors.Count; i++)
                auxiliaryAnchors[i].Follow();
        }

        public void ApplyProjectileOrigins()
        {
            for (int i = 0; i < projectileOrigins.Count; i++)
                projectileOrigins[i].Apply();
        }

        public void RestoreProjectileOrigins()
        {
            for (int i = 0; i < projectileOrigins.Count; i++)
                projectileOrigins[i].Restore();
            projectileOrigins.Clear();
            projectileOriginOwners.Clear();
        }

        public bool IsNearComputerKey(List<Collider> keyColliders, float range)
        {
            if (marker == null || keyColliders == null || keyColliders.Count == 0)
                return false;

            Vector3 markerCenter = GetOriginalMarkerCenter();
            float rangeSquared = range * range;
            for (int i = 0; i < keyColliders.Count; i++)
            {
                Collider key = keyColliders[i];
                if (key == null || !key.enabled || !key.gameObject.activeInHierarchy)
                    continue;

                Vector3 nearestPoint = key.ClosestPoint(markerCenter);
                if ((nearestPoint - markerCenter).sqrMagnitude <= rangeSquared)
                    return true;
            }
            return false;
        }

        public bool IsNearComputerTerminal(List<Transform> terminals, float range)
        {
            if (marker == null || terminals == null || terminals.Count == 0)
                return false;

            Vector3 markerCenter = GetOriginalMarkerCenter();
            float rangeSquared = range * range;
            for (int i = 0; i < terminals.Count; i++)
            {
                Transform terminal = terminals[i];
                if (terminal != null && (terminal.position - markerCenter).sqrMagnitude <= rangeSquared)
                    return true;
            }
            return false;
        }

        public void SyncPose()
        {
            foreach (KeyValuePair<Transform, Transform> pair in mapping)
            {
                if (pair.Key == null || pair.Value == null)
                    continue;
                pair.Value.localPosition = pair.Key.localPosition;
                pair.Value.localRotation = pair.Key.localRotation;
                pair.Value.localScale = pair.Key.localScale;
            }

            for (int i = 0; i < dockAnchors.Count; i++)
                dockAnchors[i].Sync();
        }

        private sealed class DockAnchorPoseBinding
        {
            public readonly Transform Source;
            public readonly Transform Proxy;
            private readonly Transform sourceRoot;

            public DockAnchorPoseBinding(Transform sourceRoot, Transform source, Transform proxy)
            {
                this.sourceRoot = sourceRoot;
                Source = source;
                Proxy = proxy;
            }

            public void Sync()
            {
                if (sourceRoot == null || Source == null || Proxy == null)
                    return;

                Proxy.localPosition = sourceRoot.InverseTransformPoint(Source.position);
                Proxy.localRotation = Quaternion.Inverse(sourceRoot.rotation) * Source.rotation;
                Vector3 rootScale = sourceRoot.lossyScale;
                Vector3 sourceScale = Source.lossyScale;
                Proxy.localScale = new Vector3(
                    Mathf.Approximately(rootScale.x, 0f) ? 1f : sourceScale.x / rootScale.x,
                    Mathf.Approximately(rootScale.y, 0f) ? 1f : sourceScale.y / rootScale.y,
                    Mathf.Approximately(rootScale.z, 0f) ? 1f : sourceScale.z / rootScale.z);
            }
        }

        private sealed class TransformPoseBinding
        {
            private readonly Transform source;
            private readonly Transform proxy;
            private readonly Vector3 originalLocalPosition;
            private readonly Quaternion originalLocalRotation;
            private readonly Vector3 originalLocalScale;

            public TransformPoseBinding(Transform source, Transform proxy)
            {
                this.source = source;
                this.proxy = proxy;
                originalLocalPosition = source.localPosition;
                originalLocalRotation = source.localRotation;
                originalLocalScale = source.localScale;
            }

            public void Restore()
            {
                if (source == null)
                    return;

                source.localPosition = originalLocalPosition;
                source.localRotation = originalLocalRotation;
                source.localScale = originalLocalScale;
            }

            public void Follow()
            {
                if (source == null || proxy == null)
                    return;

                source.position = proxy.position;
                source.rotation = proxy.rotation;
            }
        }

        private sealed class ProjectileOriginBinding
        {
            private readonly MonoBehaviour owner;
            private readonly FieldInfo field;
            private readonly Transform original;
            private readonly Transform proxy;

            public ProjectileOriginBinding(MonoBehaviour owner, FieldInfo field,
                Transform original, Transform proxy)
            {
                this.owner = owner;
                this.field = field;
                this.original = original;
                this.proxy = proxy;
            }

            public void Apply()
            {
                if (owner != null && proxy != null)
                    field.SetValue(owner, proxy);
            }

            public void Restore()
            {
                if (owner != null)
                    field.SetValue(owner, original);
            }
        }
    }

    private sealed class BodyDockPositionBinding
    {
        private readonly BodyDockPositions docks;
        private readonly Transform originalLeft;
        private readonly Transform originalRight;
        private readonly Transform proxyLeft;
        private readonly Transform proxyRight;

        public BodyDockPositionBinding(BodyDockPositions docks, Transform originalLeft,
            Transform originalRight, Transform proxyLeft, Transform proxyRight)
        {
            this.docks = docks;
            this.originalLeft = originalLeft;
            this.originalRight = originalRight;
            this.proxyLeft = proxyLeft;
            this.proxyRight = proxyRight;
        }

        public void Apply()
        {
            if (docks == null)
                return;

            docks.leftHandTransform = proxyLeft;
            docks.rightHandTransform = proxyRight;
        }

        public void Restore()
        {
            if (docks == null)
                return;

            if (docks.leftHandTransform == proxyLeft)
                docks.leftHandTransform = originalLeft;
            if (docks.rightHandTransform == proxyRight)
                docks.rightHandTransform = originalRight;
        }

        public Transform GetOriginalHand(bool leftHand)
        {
            return leftHand ? originalLeft : originalRight;
        }

        public Transform GetProxyHand(bool leftHand)
        {
            return leftHand ? proxyLeft : proxyRight;
        }
    }

    private sealed class HeldEquipmentBinding
    {
        public readonly IHoldableObject Holdable;
        private readonly Transform item;
        private readonly Rigidbody body;
        private Transform currentAnchor;
        private Transform visualAnchor;
        private Transform restoreAnchor;
        private Vector3 localPosition;
        private Quaternion localRotation;
        private Vector3 lastPosition;
        private Quaternion lastRotation;
        private bool hasLocalPose;
        private bool hasLastPose;

        public HeldEquipmentBinding(IHoldableObject holdable, Transform item,
            Transform currentAnchor, Transform visualAnchor, Transform restoreAnchor)
        {
            Holdable = holdable;
            this.item = item;
            body = item != null ? item.GetComponent<Rigidbody>() : null;
            SetAnchors(currentAnchor, visualAnchor, restoreAnchor);
        }

        public void SetAnchors(Transform newCurrentAnchor, Transform newVisualAnchor,
            Transform newRestoreAnchor)
        {
            if (newCurrentAnchor == null || newVisualAnchor == null || newRestoreAnchor == null
                || item == null)
                return;

            if (!hasLocalPose || (currentAnchor != newCurrentAnchor && visualAnchor != null))
            {
                currentAnchor = newCurrentAnchor;
                localPosition = currentAnchor.InverseTransformPoint(item.position);
                localRotation = Quaternion.Inverse(currentAnchor.rotation) * item.rotation;
                hasLocalPose = true;
            }
            currentAnchor = newCurrentAnchor;
            visualAnchor = newVisualAnchor;
            restoreAnchor = newRestoreAnchor;
        }

        public void Apply()
        {
            if (item == null || visualAnchor == null)
                return;

            SetWorldPose(visualAnchor.TransformPoint(localPosition),
                visualAnchor.rotation * localRotation);
        }

        public void Restore(bool preserveLastPose)
        {
            if (item == null)
                return;

            if (preserveLastPose && hasLastPose)
                SetWorldPose(lastPosition, lastRotation);
            else if (restoreAnchor != null)
                SetWorldPose(restoreAnchor.TransformPoint(localPosition),
                    restoreAnchor.rotation * localRotation);
        }

        public void Deactivate()
        {
            visualAnchor = null;
        }

        private void SetWorldPose(Vector3 position, Quaternion rotation)
        {
            if (item == null)
                return;

            item.SetPositionAndRotation(position, rotation);
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
            }
            lastPosition = position;
            lastRotation = rotation;
            hasLastPose = true;
        }
    }

    private sealed class HeldThrowableBinding
    {
        public readonly GorillaThrowable Throwable;
        public readonly Transform OriginalGrabTransform;
        public readonly Transform OriginalFollowTransform;
        private readonly FieldInfo followTransformField;
        private Transform target;

        private readonly Transform originalPoseAnchor;
        private readonly Vector3 originalLocalPosition;
        private readonly Quaternion originalLocalRotation;
        private readonly Vector3 projectileVisualOffset;
        private readonly Rigidbody body;
        private Vector3 lastAlignedPosition;
        private Quaternion lastAlignedRotation;
        private bool hasAlignedPose;

        public bool IsActive
        {
            get { return target != null; }
        }

        public HeldThrowableBinding(GorillaThrowable throwable, Transform target,
            Transform originalGrabTransform, Transform originalFollowTransform,
            FieldInfo followTransformField)
        {
            Throwable = throwable;
            this.target = target;
            this.followTransformField = followTransformField;
            OriginalGrabTransform = originalGrabTransform;
            OriginalFollowTransform = originalFollowTransform;
            projectileVisualOffset = throwable is GorillaFireball || throwable is GorillaThrowingRock
                ? HeldProjectileVisualOffset : Vector3.zero;
            originalPoseAnchor = originalFollowTransform != null
                ? originalFollowTransform : originalGrabTransform;
            body = throwable != null ? throwable.GetComponent<Rigidbody>() : null;
            if (throwable != null && originalPoseAnchor != null)
            {
                originalLocalPosition = originalPoseAnchor.InverseTransformPoint(throwable.transform.position);
                originalLocalRotation = Quaternion.Inverse(originalPoseAnchor.rotation)
                    * throwable.transform.rotation;
            }
            else
            {
                originalLocalPosition = Vector3.zero;
                originalLocalRotation = Quaternion.identity;
            }
        }

        public void SetTarget(Transform newTarget)
        {
            if (newTarget != null)
                target = newTarget;
            Apply();
        }

        public void Apply()
        {
            if (Throwable == null || target == null)
                return;

            Throwable.grabbingTransform = target;
            if (followTransformField != null)
            {
                try
                {
                    followTransformField.SetValue(Throwable, target);
                }
                catch (Exception)
                {
                    // The public grab transform still provides a fallback if the private field changes.
                }
            }

            UpdateHeldWorldPose();
        }

        public void Restore(bool preserveLastPose)
        {
            if (Throwable == null)
                return;

            if (preserveLastPose && hasAlignedPose)
                SetWorldPose(lastAlignedPosition, lastAlignedRotation);
            else if (Throwable.isHeld && originalPoseAnchor != null)
                SetWorldPose(originalPoseAnchor.TransformPoint(originalLocalPosition),
                    originalPoseAnchor.rotation * originalLocalRotation);

            RestoreReferences();
        }

        public void RestoreReferences()
        {
            if (Throwable == null)
                return;

            if (target != null && Throwable.grabbingTransform == target)
                Throwable.grabbingTransform = OriginalGrabTransform;
            if (target != null && followTransformField != null)
            {
                try
                {
                    if (followTransformField.GetValue(Throwable) as Transform == target)
                        followTransformField.SetValue(Throwable, OriginalFollowTransform);
                }
                catch (Exception)
                {
                    // The throwable may have been destroyed while the mod was being disabled.
                }
            }
        }

        public void Deactivate()
        {
            Restore(false);
            target = null;
        }

        private void UpdateHeldWorldPose()
        {
            if (Throwable == null || target == null)
                return;

            Vector3 position = target.TransformPoint(originalLocalPosition + projectileVisualOffset);
            Quaternion rotation = target.rotation * originalLocalRotation;
            SetWorldPose(position, rotation);
            lastAlignedPosition = position;
            lastAlignedRotation = rotation;
            hasAlignedPose = true;
        }

        private void SetWorldPose(Vector3 position, Quaternion rotation)
        {
            if (Throwable == null)
                return;

            Throwable.transform.SetPositionAndRotation(position, rotation);
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
            }
        }
    }

    private sealed class RendererBoneBackup
    {
        public readonly SkinnedMeshRenderer Renderer;
        public readonly Transform[] OriginalBones;
        public readonly Transform OriginalRootBone;

        public RendererBoneBackup(SkinnedMeshRenderer renderer, Transform[] originalBones,
            Transform originalRootBone)
        {
            Renderer = renderer;
            OriginalBones = originalBones;
            OriginalRootBone = originalRootBone;
        }
    }
}
