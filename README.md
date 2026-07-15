# Atom Animation (Meta Quest)

A Unity project that plays back a molecular-dynamics trajectory (201 XYZ frames,
~3,000 atoms) as an animated 3D molecule, configured to build as a Meta Quest VR
app that can be installed with Meta Quest Developer Hub (MQDH).

## Requirements

- **Unity 2022.3.24f1** (or a later 2022.3 LTS patch) with the **Android Build
  Support** module, including *Android SDK & NDK Tools* and *OpenJDK*
  (tick them in Unity Hub when installing the editor).
- **Meta Quest Developer Hub** with a Meta developer account.
- A Quest headset with **developer mode** enabled (done from the Meta Horizon
  phone app, or via MQDH > Device Manager).

## Building the APK

1. Open the project in Unity Hub. On first open, Unity resolves the packages
   (XR Plugin Management + Oculus XR plugin) and regenerates
   `Packages/packages-lock.json` — this is expected.
2. **File > Build Settings**, select **Android**, and click **Switch Platform**.
3. Click **Build** and choose an output path, e.g. `Builds/AtomAnimation.apk`.

Everything else is already configured in the repo:

- XR Plug-in Management with the **Oculus** loader enabled for Android
  (multiview stereo rendering).
- IL2CPP scripting backend, ARM64 only, min SDK 29, OpenGL ES 3, linear color
  space, ASTC textures.
- `SampleScene` registered in the build scene list.

## Installing with Meta Quest Developer Hub

1. Connect the headset over USB and allow the connection in the headset.
2. In MQDH, open **Device Manager** and confirm the headset shows as connected.
3. Drag the built APK onto the device (or use **Add Build** / *Install APK*).
4. In the headset, the app appears in the library under
   **Apps > filter: Unknown Sources** as *Atom Animation*.

## What you see in the headset

The molecule floats about 1.8 m in front of your starting position at chest
height, scaled to about 1.5 m, playing the 201-frame trajectory on loop at
20 frames/second with interpolation. Red spheres are hydrogen, blue are carbon
(colors come from `Assets/Materials/Mat_H.mat` / `Mat_C.mat`).

Playback speed, molecule size, and atom sizes are tunable on the
`MolecularManager` object in `SampleScene` (`XYZLoader` component).

## Project layout

| Path | Purpose |
| --- | --- |
| `Assets/Scripts/XYZLoader.cs` | Loads, unwraps, and animates the trajectory |
| `Assets/Scripts/VRHeadTracking.cs` | Drives the camera from the headset pose |
| `Assets/Resources/txt_frames/` | Trajectory data (XYZ format, one file per frame) |
| `Assets/XR/` | XR Plug-in Management / Oculus loader configuration |
| `Tools/rename_script.py` | One-off helper that renamed `.xyz` files to `.txt` |

### Notes on the trajectory data

The simulation uses periodic boundary conditions, so raw coordinates jump
across the ~97 Å box when atoms drift over an edge. `XYZLoader` unwraps these
jumps at load time so playback is continuous. Parsing runs on a background
thread and uses invariant culture, so it works on any OS locale.
