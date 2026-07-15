using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Loads an XYZ molecular-dynamics trajectory from Resources/txt_frames and plays
/// it back as animated spheres parented to this object. Positions are unwrapped
/// across periodic boundaries, centred and scaled so the whole molecule fits
/// inside <see cref="targetSize"/> metres, which makes scene placement trivial
/// (just move/rotate this GameObject).
/// </summary>
public class XYZLoader : MonoBehaviour
{
    public GameObject atomPrefab;
    public Material hydrogenMaterial;
    public Material carbonMaterial;

    [Tooltip("Playback speed in trajectory frames per second.")]
    public float framesPerSecond = 20f;

    [Tooltip("The molecule is scaled so its largest dimension matches this size in metres.")]
    public float targetSize = 1.5f;

    [Tooltip("Hydrogen sphere diameter in trajectory units (angstroms).")]
    public float hydrogenDiameter = 0.5f;

    [Tooltip("Carbon sphere diameter in trajectory units (angstroms).")]
    public float carbonDiameter = 0.8f;

    // Periodic-boundary unwrapping is only applied along axes at least this large,
    // so thin axes (e.g. a 2D sheet) are not mistaken for a periodic box.
    const float MinPeriodicBoxSize = 10f;

    Transform[] atoms;
    Vector3[][] frames;   // local-space positions, centred and scaled
    bool[] isHydrogen;
    float scaleFactor = 1f;
    float playbackTime;
    bool ready;

    IEnumerator Start()
    {
        // Resources API is main-thread only, so grab the raw text here and hand
        // the ~600k-line parse off to a worker thread to avoid a long startup hitch.
        TextAsset[] assets = Resources.LoadAll<TextAsset>("txt_frames");
        Array.Sort(assets, (a, b) => string.CompareOrdinal(a.name, b.name));
        if (assets.Length == 0)
        {
            Debug.LogError("XYZLoader: no trajectory files found in Resources/txt_frames.");
            yield break;
        }

        var texts = new string[assets.Length];
        for (int i = 0; i < assets.Length; i++)
        {
            texts[i] = assets[i].text;
        }

        Task<ParseResult> task = Task.Run(() => ParseAndPrepare(texts, targetSize, MinPeriodicBoxSize));
        while (!task.IsCompleted)
        {
            yield return null;
        }

        if (task.Exception != null)
        {
            Debug.LogError($"XYZLoader: failed to parse trajectory: {task.Exception.GetBaseException()}");
            yield break;
        }

        ParseResult result = task.Result;
        foreach (string warning in result.warnings)
        {
            Debug.LogWarning($"XYZLoader: {warning}");
        }

        if (result.frames.Length == 0 || result.frames[0].Length == 0)
        {
            Debug.LogError("XYZLoader: trajectory contained no usable frames.");
            yield break;
        }

        frames = result.frames;
        isHydrogen = result.isHydrogen;
        scaleFactor = result.scaleFactor;

        CreateAtoms();
        ready = true;
        Debug.Log($"XYZLoader: loaded {frames.Length} frames with {frames[0].Length} atoms each.");
    }

    void Update()
    {
        if (!ready)
        {
            return;
        }

        playbackTime = Mathf.Repeat(playbackTime + Time.deltaTime * framesPerSecond, frames.Length);
        // Mathf.Repeat can return exactly frames.Length due to float rounding.
        int current = Mathf.Min((int)playbackTime, frames.Length - 1);
        int next = current + 1;
        float t = playbackTime - current;
        if (next >= frames.Length)
        {
            // The trajectory is not cyclic, so snap instead of interpolating
            // from the last frame back to the first.
            next = current;
            t = 0f;
        }

        Vector3[] a = frames[current];
        Vector3[] b = frames[next];
        for (int i = 0; i < atoms.Length; i++)
        {
            atoms[i].localPosition = Vector3.LerpUnclamped(a[i], b[i], t);
        }
    }

    void CreateAtoms()
    {
        int atomCount = frames[0].Length;
        atoms = new Transform[atomCount];

        // Unity's built-in sphere is ~760 triangles, which is far too heavy for
        // thousands of instances on mobile VR. Use a shared low-poly icosphere.
        Mesh sphereMesh = BuildIcosphere(1);

        float hydrogenScale = hydrogenDiameter * scaleFactor;
        float carbonScale = carbonDiameter * scaleFactor;

        for (int i = 0; i < atomCount; i++)
        {
            GameObject atom = Instantiate(atomPrefab, transform);
            atom.transform.localPosition = frames[0][i];
            atom.transform.localScale = Vector3.one * (isHydrogen[i] ? hydrogenScale : carbonScale);

            MeshFilter filter = atom.GetComponent<MeshFilter>();
            if (filter != null)
            {
                filter.sharedMesh = sphereMesh;
            }

            // sharedMaterial keeps every atom on one of two material instances so
            // GPU instancing can batch them; .material would create 3k copies.
            atom.GetComponent<Renderer>().sharedMaterial = isHydrogen[i] ? hydrogenMaterial : carbonMaterial;

            atoms[i] = atom.transform;
        }
    }

    struct ParseResult
    {
        public Vector3[][] frames;
        public bool[] isHydrogen;
        public float scaleFactor;
        public List<string> warnings;
    }

    static ParseResult ParseAndPrepare(string[] texts, float targetSize, float minPeriodicBoxSize)
    {
        var warnings = new List<string>();
        var rawFrames = new List<Vector3[]>();
        List<bool> hydrogenFlags = null;
        char[] separators = { ' ', '\t' };

        for (int f = 0; f < texts.Length; f++)
        {
            string[] lines = texts[f].Split('\n');
            var positions = new List<Vector3>(hydrogenFlags?.Count ?? 4096);
            bool firstFrame = hydrogenFlags == null;
            var flags = firstFrame ? new List<bool>(4096) : null;

            // Skip the two XYZ header lines (atom count and comment).
            for (int j = 2; j < lines.Length; j++)
            {
                if (string.IsNullOrWhiteSpace(lines[j]))
                {
                    continue;
                }

                string[] parts = lines[j].Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                {
                    continue;
                }

                // Invariant culture: these files always use '.' as the decimal
                // separator, regardless of the OS locale.
                float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
                float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
                float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
                positions.Add(new Vector3(x, y, z));
                flags?.Add(parts[0] == "H");
            }

            if (firstFrame)
            {
                hydrogenFlags = flags;
            }
            else if (positions.Count != hydrogenFlags.Count)
            {
                warnings.Add($"frame {f} has {positions.Count} atoms, expected {hydrogenFlags.Count}; skipping it.");
                continue;
            }

            rawFrames.Add(positions.ToArray());
        }

        if (rawFrames.Count == 0 || rawFrames[0].Length == 0)
        {
            return new ParseResult
            {
                frames = Array.Empty<Vector3[]>(),
                isHydrogen = Array.Empty<bool>(),
                scaleFactor = 1f,
                warnings = warnings,
            };
        }

        Vector3[][] frames = rawFrames.ToArray();

        UnwrapPeriodicBoundaries(frames, minPeriodicBoxSize);

        // Centre the whole trajectory at the origin and scale it to targetSize.
        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        foreach (Vector3[] frame in frames)
        {
            foreach (Vector3 p in frame)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        Vector3 center = (min + max) * 0.5f;
        Vector3 extent = max - min;
        float largest = Mathf.Max(extent.x, extent.y, extent.z);
        float scale = largest > 0f ? targetSize / largest : 1f;

        for (int f = 0; f < frames.Length; f++)
        {
            Vector3[] frame = frames[f];
            for (int i = 0; i < frame.Length; i++)
            {
                frame[i] = (frame[i] - center) * scale;
            }
        }

        return new ParseResult
        {
            frames = frames,
            isHydrogen = hydrogenFlags.ToArray(),
            scaleFactor = scale,
            warnings = warnings,
        };
    }

    /// <summary>
    /// The simulation uses periodic boundary conditions, so atoms that drift past
    /// a box edge reappear on the opposite side and would visibly teleport during
    /// playback. Detect those wrap-around jumps (more than half a box length in a
    /// single frame step) and undo them so motion stays continuous.
    /// </summary>
    static void UnwrapPeriodicBoundaries(Vector3[][] frames, float minPeriodicBoxSize)
    {
        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        foreach (Vector3[] frame in frames)
        {
            foreach (Vector3 p in frame)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        Vector3 box = max - min;
        bool unwrapX = box.x >= minPeriodicBoxSize;
        bool unwrapY = box.y >= minPeriodicBoxSize;
        bool unwrapZ = box.z >= minPeriodicBoxSize;
        if (!unwrapX && !unwrapY && !unwrapZ)
        {
            return;
        }

        for (int f = 1; f < frames.Length; f++)
        {
            Vector3[] previous = frames[f - 1];
            Vector3[] current = frames[f];
            for (int i = 0; i < current.Length; i++)
            {
                Vector3 p = current[i];
                Vector3 d = p - previous[i];
                if (unwrapX) p.x -= box.x * Mathf.Round(d.x / box.x);
                if (unwrapY) p.y -= box.y * Mathf.Round(d.y / box.y);
                if (unwrapZ) p.z -= box.z * Mathf.Round(d.z / box.z);
                current[i] = p;
            }
        }
    }

    /// <summary>
    /// Builds an icosphere with a diameter of 1 so it is a drop-in replacement for
    /// Unity's built-in sphere. One subdivision gives 80 triangles per atom.
    /// </summary>
    static Mesh BuildIcosphere(int subdivisions)
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

        var vertices = new List<Vector3>
        {
            new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
            new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
            new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1),
        };

        var triangles = new List<int>
        {
            0, 11, 5,   0, 5, 1,    0, 1, 7,    0, 7, 10,   0, 10, 11,
            1, 5, 9,    5, 11, 4,   11, 10, 2,  10, 7, 6,   7, 1, 8,
            3, 9, 4,    3, 4, 2,    3, 2, 6,    3, 6, 8,    3, 8, 9,
            4, 9, 5,    2, 4, 11,   6, 2, 10,   8, 6, 7,    9, 8, 1,
        };

        var midpointCache = new Dictionary<long, int>();
        int GetMidpoint(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (midpointCache.TryGetValue(key, out int index))
            {
                return index;
            }

            vertices.Add((vertices[a] + vertices[b]) * 0.5f);
            index = vertices.Count - 1;
            midpointCache.Add(key, index);
            return index;
        }

        for (int s = 0; s < subdivisions; s++)
        {
            var next = new List<int>(triangles.Count * 4);
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                int ab = GetMidpoint(a, b);
                int bc = GetMidpoint(b, c);
                int ca = GetMidpoint(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }

            triangles = next;
        }

        var normals = new Vector3[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 n = vertices[i].normalized;
            normals[i] = n;
            vertices[i] = n * 0.5f;
        }

        var mesh = new Mesh { name = "AtomIcosphere" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        return mesh;
    }
}
