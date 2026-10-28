using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

[BepInPlugin("purplemoonieee.leg.mod.utilla", "Legs Mod", "1.0.3")]
[BepInDependency("org.legoandmars.gorillatag.utilla", "1.5.0")]
public sealed class ModSidedLegArmsPlugin : BaseUnityPlugin
{
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
    private float nextComputerKeyLookupTime;
    private string lastLoggedInteractionTargetCounts;
    private float nextPresenceAttemptTime;
    private bool presenceConfirmed;
    private bool legsEnabled = true;
#if !LEGS_ALWAYS_ON
    private bool leftPrimaryButtonWasPressed;
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
        bool leftPrimaryPressed = false;
        UnityEngine.XR.InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        bool hasLeftPrimaryState = leftHand.isValid
            && leftHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out leftPrimaryPressed);
        bool xrControllerToggle = hasLeftPrimaryState && leftPrimaryPressed && !leftPrimaryButtonWasPressed;
        leftPrimaryButtonWasPressed = hasLeftPrimaryState && leftPrimaryPressed;
        XRController inputSystemLeftHand = XRController.leftHand;
        ButtonControl inputSystemPrimary = inputSystemLeftHand != null
            ? inputSystemLeftHand.TryGetChildControl<ButtonControl>("primaryButton") : null;
        bool inputSystemControllerToggle = inputSystemPrimary != null
            && inputSystemPrimary.wasPressedThisFrame;
        bool controllerToggle = xrControllerToggle || inputSystemControllerToggle;

        if (!loggedToggleInputDiagnostic && Log != null && Time.unscaledTime >= 3f)
        {
            Log.LogInfo("Toggle input diagnostics: left XR device valid=" + leftHand.isValid
                + ", name=" + leftHand.name + ", XR primary supported=" + hasLeftPrimaryState
                + ", Input System left controller=" + (inputSystemLeftHand != null)
                + ", Input System primary control=" + (inputSystemPrimary != null) + ".");
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

        if (!legsEnabled)
        {
            RestoreAllRigs();
            RemoveDestroyedRigStates();
            return;
        }

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
            HandRaise != null ? Mathf.Clamp(HandRaise.Value, -0.25f, 0.25f) : 0.0290f,
            HandTowardTorso != null ? Mathf.Clamp(HandTowardTorso.Value, 0f, 0.2f) : 0.08f,
            atInteractionStation);
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
            RestoreVisualRig(state);
            return false;
        }

        state.HandProxies.Add(new HandProxy(leftRoot, leftProxyRoot, leftMarker, leftMap));
        state.HandProxies.Add(new HandProxy(rightRoot, rightProxyRoot, rightMarker, rightMap));

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
            RestoreVisualRig(state);
            return false;
        }

        state.VisualRigReady = true;
        if (!state.LoggedVisualRig)
        {
            if (Log != null)
                Log.LogInfo("Created animated hand proxies for a compatible rig; controller and gameplay transforms are unchanged.");
            state.LoggedVisualRig = true;
        }
        return true;
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
            proxy.SyncPose();
            if (alignAtComputer)
                proxy.ProxyRoot.position = proxy.SourceRoot.position;
            else
            {
                Vector3 markerOffset = proxy.SourceRoot.TransformVector(proxy.RootToMarkerLocal);
                proxy.ProxyRoot.position = proxy.SourceRoot.position + markerOffset * 0.5f + offset;
            }
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

    private static void RestoreVisualRig(RigState state)
    {
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
        RestoreVisualRig(state);
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
                staleRigs.Add(pair.Key);
        }
        for (int i = 0; i < staleRigs.Count; i++)
            states.Remove(staleRigs[i]);
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
            RestoreVisualRig(state);
        }
        states.Clear();
    }

    private sealed class HandProxy
    {
        public readonly Transform SourceRoot;
        public readonly Transform ProxyRoot;
        public readonly Vector3 RootToMarkerLocal;
        private readonly SphereCollider marker;
        private readonly Dictionary<Transform, Transform> mapping;

        public HandProxy(Transform sourceRoot, Transform proxyRoot, SphereCollider marker,
            Dictionary<Transform, Transform> mapping)
        {
            SourceRoot = sourceRoot;
            ProxyRoot = proxyRoot;
            this.marker = marker;
            RootToMarkerLocal = sourceRoot.InverseTransformVector(marker.bounds.center - sourceRoot.position);
            this.mapping = mapping;
        }

        public bool IsNearComputerKey(List<Collider> keyColliders, float range)
        {
            if (marker == null || keyColliders == null || keyColliders.Count == 0)
                return false;

            Vector3 markerCenter = marker.bounds.center;
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

            Vector3 markerCenter = marker.bounds.center;
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
