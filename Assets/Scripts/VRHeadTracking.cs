using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Drives the camera transform from the XR headset pose. With the XR plug-in
/// system the camera is no longer tracked automatically, so without this (or a
/// TrackedPoseDriver) the view would stay locked to the user's face.
/// Uses only core UnityEngine.XR APIs, so it works with any loader and either
/// input backend.
/// </summary>
public class VRHeadTracking : MonoBehaviour
{
    [Tooltip("Camera height used in the editor or when the device reports poses relative to the head instead of the floor.")]
    public float fallbackEyeHeight = 1.6f;

    static readonly List<XRNodeState> s_NodeStates = new List<XRNodeState>();
    static readonly List<XRInputSubsystem> s_InputSubsystems = new List<XRInputSubsystem>();

    bool floorOrigin;
    bool originConfigured;

    void Start()
    {
        // Sensible view when running without a headset (e.g. editor play mode).
        transform.localPosition = new Vector3(0f, fallbackEyeHeight, 0f);
    }

    void OnEnable()
    {
        Application.onBeforeRender += UpdatePose;
    }

    void OnDisable()
    {
        Application.onBeforeRender -= UpdatePose;
    }

    void Update()
    {
        if (!originConfigured)
        {
            ConfigureTrackingOrigin();
        }

        UpdatePose();
    }

    void ConfigureTrackingOrigin()
    {
        SubsystemManager.GetSubsystems(s_InputSubsystems);
        foreach (XRInputSubsystem subsystem in s_InputSubsystems)
        {
            if (subsystem == null || !subsystem.running)
            {
                continue;
            }

            // Prefer floor-relative tracking so the camera ends up at the user's
            // real standing height; fall back to a fixed offset in device mode.
            floorOrigin = subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor)
                || (subsystem.GetTrackingOriginMode() & TrackingOriginModeFlags.Floor) != 0;
            originConfigured = true;
            return;
        }
    }

    void UpdatePose()
    {
        InputTracking.GetNodeStates(s_NodeStates);
        foreach (XRNodeState state in s_NodeStates)
        {
            if (state.nodeType != XRNode.CenterEye)
            {
                continue;
            }

            if (state.TryGetPosition(out Vector3 position))
            {
                if (!floorOrigin)
                {
                    position.y += fallbackEyeHeight;
                }

                transform.localPosition = position;
            }

            if (state.TryGetRotation(out Quaternion rotation))
            {
                transform.localRotation = rotation;
            }

            return;
        }
    }
}
