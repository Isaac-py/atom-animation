using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class XYZLoader : MonoBehaviour
{
    public GameObject atomPrefab;
    public Material hydrogenMaterial;
    public Material carbonMaterial;

    private List<GameObject> atoms = new List<GameObject>();
    private List<List<Vector3>> allFrames = new List<List<Vector3>>();
    private List<int> atomTypes = new List<int>(); // 1 = H, 2 = C

    private float frameDelay = 0.05f; // 20 FPS
    private int currentFrame = 0;
    private LineRenderer trail;

    void Start()
    {
        LoadAllFrames();
        CreateAtoms();
        StartCoroutine(PlayAnimation());
    }

    void LoadAllFrames()
    {
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (int i = 0; i < 201; i++)
        {
            string path = $"txt_frames/frame_{i:000}";
            TextAsset txtFile = Resources.Load<TextAsset>(path);
            if (txtFile == null)
            {
                Debug.LogWarning($"❌ Missing: {path}");
                continue;
            }

            string[] lines = txtFile.text.Split('\n');
            List<Vector3> positions = new List<Vector3>();

            for (int j = 2; j < lines.Length; j++) // skip first 2 header lines
            {
                if (string.IsNullOrWhiteSpace(lines[j])) continue;
                string[] parts = lines[j].Trim().Split();
                if (parts.Length < 4) continue;

                string symbol = parts[0];
                float x = float.Parse(parts[1]);
                float y = float.Parse(parts[2]);
                float z = float.Parse(parts[3]);
                positions.Add(new Vector3(x, y, z));

                if (i == 0)
                {
                    int type = (symbol == "H") ? 1 : 2;
                    atomTypes.Add(type);
                }

                min = Vector3.Min(min, positions[positions.Count - 1]);
                max = Vector3.Max(max, positions[positions.Count - 1]);
            }

            allFrames.Add(positions);
        }

        // Normalize positions
        Vector3 center = (min + max) / 2f;
        float scale = 0.5f;

        for (int f = 0; f < allFrames.Count; f++)
        {
            for (int a = 0; a < allFrames[f].Count; a++)
            {
                allFrames[f][a] = (allFrames[f][a] - center) * scale;
            }
        }

        Debug.Log($"📦 Loaded {allFrames.Count} frames with {allFrames[0].Count} atoms each");
    }

    void CreateAtoms()
    {
        int atomCount = allFrames[0].Count;

        for (int i = 0; i < atomCount; i++)
        {
            GameObject atom = Instantiate(atomPrefab, allFrames[0][i], Quaternion.identity);

            int type = atomTypes[i];
            atom.GetComponent<Renderer>().material = (type == 1) ? hydrogenMaterial : carbonMaterial;

            atoms.Add(atom);

            /*
            if (i == 0) // optional: visualize trajectory for atom 0
            {
                trail = atom.AddComponent<LineRenderer>();
                trail.positionCount = allFrames.Count;
                trail.widthMultiplier = 0.02f;
                trail.material = new Material(Shader.Find("Sprites/Default"));
                trail.startColor = Color.green;
                trail.endColor = Color.green;

                for (int f = 0; f < allFrames.Count; f++)
                {
                    trail.SetPosition(f, allFrames[f][i]);
                }
            } */
        }
    }

    IEnumerator PlayAnimation()
    {
        while (true)
        {
            List<Vector3> frame = allFrames[currentFrame];
            for (int i = 0; i < atoms.Count; i++)
            {
                atoms[i].transform.position = frame[i];
            }

            currentFrame = (currentFrame + 1) % allFrames.Count;
            yield return new WaitForSeconds(frameDelay);
        }
    }
}
