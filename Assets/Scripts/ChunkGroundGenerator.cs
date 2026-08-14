using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ChunkGroundGenerator : MonoBehaviour
{
    [Header("Ландшафт")]
    public int resolution = 128;          // render mesh, жёстко клампится [8..256]
    public int collisionResolution = 64;  // physics mesh, клампится [8..128]
    public float heightScale = 10f;
    public float noiseScale = 0.02f;

    [Header("Стык земли и дороги")]
    [Tooltip("Автоопределение высоты асфальта (по коллайдеру дороги или её рендереру)")]
    public bool autoRoadHeight = true;
    [Tooltip("Ручная поправка высоты земли у дороги, если авто не попало")]
    public float roadHeightOffset = 0f;
    [Tooltip("Насколько земля заходит ПОД асфальт, чтобы не было щели")]
    public float roadTuck = 0.03f;

    private float roadBaseY;

    [Header("Препятствия (дороги, объекты)")]
    public float flatZoneRadius = 8f;
    public float blendZone = 15f;
    public float edgeBlendZone = 20f;
    public float terrainPadding = 40f;

    [Header("Трава: Patch + GPU Instancing")]
    public Material grassMaterial;
    public float grassPatchSize = 4f;
    [Range(1, 4)] public int grassMeshVariants = 2;
    [Range(8, 96)] public int bladesLOD0 = 48;   // crossed quads
    [Range(4, 48)] public int bladesLOD1 = 16;   // single quads
    public float grassMinScale = 0.7f;
    public float grassMaxScale = 1.2f;
    public float grassWidth = 0.35f;

    [Header("Трава: густота и зоны")]
    [Range(0.1f, 1f)] public float grassDensity = 0.9f;
    public float grassRoadMargin = 2.5f;
    public float grassTransitionZone = 8f;
    [Range(0f, 1f)] public float grassNearRoadDensity = 0.2f;
    [Range(0.05f, 1f)] public float lod0Density = 1f;
    [Range(0.05f, 1f)] public float lod1Density = 0.5f;
    [Range(0.05f, 1f)] public float lod2Density = 0.2f;

    [Header("Трава: тени")]
    [Tooltip("Отбрасывает ли трава тени (дёшево: только LOD0)")]
    public bool grassCastShadows = true;
    [Range(0, 2)] public int shadowMaxLOD = 0;

    [Header("Трава: LOD дистанции")]
    public float grassLOD0Distance = 25f;
    public float grassLOD1Distance = 55f;
    public float grassLOD2Distance = 100f;
    public float grassRenderDistance = 110f;

    [Header("Трава: оптимизация")]
    public int maxGrassUpdateInterval = 2;

    [Header("Окружение (Деревья, камни)")]
    public float environmentGridStep = 12f;
    public List<EnvironmentObject> environmentPrefabs;

    [System.Serializable]
    public class EnvironmentObject
    {
        public GameObject prefab;
        [Range(0f, 1f)] public float spawnProbability = 0.05f;
        public float minScale = 0.8f;
        public float maxScale = 1.5f;
        public bool alignToTerrain = false;
        public float yOffset = -0.1f;
    }

    [System.NonSerialized] public DistanceField distanceField;

    private const int LOD_COUNT = 3;
    private const int MAX_PER_STREAM = 4096;
    private const int BATCH_SIZE = 1023;

    private float terrainStartX, terrainEndX, terrainStartZ, terrainEndZ;
    private float width, length;
    private float perlinOffsetX, perlinOffsetZ;
    private int terrainRes;

    private Camera cachedCamera;
    private Vector3 lastRebuildPosition;
    private Quaternion lastCameraRot;
    private Plane[] frustumPlanes = new Plane[6];
    private float rebuildThresholdSqr;
    private int grassUpdateTimer;
    private bool grassDirty = true;
    private bool isInitialized;

    private Mesh terrainMesh;
    private Mesh collisionMesh;
    private Vector3[] terrainVertices;

    // ---------- Трава: flat array патчей ----------
    private struct GrassPatch
    {
        public Vector3 worldPos;
        public float rotC;
        public float rotS;
        public float scale;
        public float rand01;   // детерминированное прореживание LOD
        public byte variant;
        public bool alive;
    }

    private GrassPatch[] patches;
    private int patchGridX, patchGridZ;

    // ---------- Трава: стримы отрисовки (LOD x variant) ----------
    private int variantCount;
    private int streamCount;
    private Mesh[] streamMeshes;
    private Matrix4x4[][] streamMatrices;
    private int[] streamCounts;
    private readonly Matrix4x4[] batchMatrices = new Matrix4x4[BATCH_SIZE];
    private float patchCullRadius;

    // ============================================================

    void Start()
    {
        cachedCamera = Camera.main;

        if (grassMaterial != null) grassMaterial.enableInstancing = true;
        else Debug.LogWarning($"[{name}] Grass Material не назначен. Генерация травы пропущена.", this);

        perlinOffsetX = transform.position.x * 0.13f + 1000f;
        perlinOffsetZ = transform.position.z * 0.17f + 1000f;

        float rt = Mathf.Max(1f, grassPatchSize * 0.5f);
        rebuildThresholdSqr = rt * rt;
        lastRebuildPosition = Vector3.zero;
        if (cachedCamera != null) lastCameraRot = cachedCamera.transform.rotation;

        if (!isInitialized) InitChunk();
    }

    void OnDestroy()
    {
        if (terrainMesh != null) Destroy(terrainMesh);
        if (collisionMesh != null) Destroy(collisionMesh);
        if (streamMeshes != null)
        {
            for (int i = 0; i < streamMeshes.Length; i++)
                if (streamMeshes[i] != null) Destroy(streamMeshes[i]);
        }
    }

    public void InitChunk()
    {
        if (isInitialized) return;

        RoadChunk road = GetComponent<RoadChunk>();
        List<Renderer> obstacles = new List<Renderer>();
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r == null || r.gameObject == gameObject) continue;
            if (road != null && ((road.startPoint != null && r.gameObject == road.startPoint.gameObject) ||
                                 (road.endPoint != null && r.gameObject == road.endPoint.gameObject))) continue;

            obstacles.Add(r);
            Vector3 localMin = transform.InverseTransformPoint(r.bounds.min);
            Vector3 localMax = transform.InverseTransformPoint(r.bounds.max);
            minX = Mathf.Min(minX, localMin.x); maxX = Mathf.Max(maxX, localMax.x);
            minZ = Mathf.Min(minZ, localMin.z); maxZ = Mathf.Max(maxZ, localMax.z);
        }

        if (minX == float.MaxValue) { minX = -20f; maxX = 20f; minZ = 0f; maxZ = 50f; }

        terrainStartX = minX - terrainPadding; terrainEndX = maxX + terrainPadding;
        terrainStartZ = minZ; terrainEndZ = maxZ;
        width = terrainEndX - terrainStartX; length = terrainEndZ - terrainStartZ;

        distanceField = new DistanceField(obstacles, terrainStartX, terrainEndX, terrainStartZ, terrainEndZ, 64, transform);
        float detected = autoRoadHeight ? DetectRoadHeight(road, obstacles) : 0f;
        roadBaseY = detected - roadTuck + roadHeightOffset;
        GenerateTerrainMesh();
        BuildTerrainCollisionMesh();
        GenerateEnvironment();
        BuildPatchMeshes();
        GenerateGrassPatches();

        isInitialized = true;
        grassDirty = true;
    }

    // ================= TERRAIN (сохранён) =================

    private float CalculateHeight(Vector3 worldPos, float localZ, float dist)
    {
        // 0 у дороги -> 1 вдали (начинаются холмы)
        float flatFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flatZoneRadius, flatZoneRadius + blendZone, dist));
        // гашение холмов у краёв чанка по Z, чтобы чанки стыковались
        float edgeFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, edgeBlendZone, Mathf.Min(localZ - terrainStartZ, terrainEndZ - localZ)));

        // У дороги земля держит ВЫСОТУ АСФАЛЬТА, вдали — ноль
        float baseH = Mathf.Lerp(roadBaseY, 0f, flatFade);

        float hills = Mathf.PerlinNoise((worldPos.x + perlinOffsetX) * noiseScale, (worldPos.z + perlinOffsetZ) * noiseScale) * heightScale;

        // Холмы включаются по мере удаления от дороги и гаснут у краёв чанка.
        // База (roadBaseY) НЕ гасится у краёв — поэтому земля остаётся в уровень
        // с дорогой даже на стыке чанков.
        return baseH + (hills - baseH) * flatFade * edgeFade;
    }

    /// Определяет высоту полотна дороги: сначала лучом в коллайдер дороги,
    /// иначе — по верху ближайшего к StartPoint рендерера дороги.
    /// иначе — по верху ближайшего к StartPoint рендерера дороги.
    /// Возвращает высоту в ЛОКАЛЬНЫХ координатах чанка (CalculateHeight работает в локале).
    private float DetectRoadHeight(RoadChunk road, List<Renderer> obstacles)
    {
        // 1) Луч сверху вниз в середину дороги: ищем коллайдер дороги внутри чанка
        if (road != null && road.startPoint != null && road.endPoint != null)
        {
            Vector3 mid = (road.startPoint.position + road.endPoint.position) * 0.5f;
            RaycastHit[] hits = Physics.RaycastAll(mid + Vector3.up * 10f, Vector3.down, 20f);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].transform.IsChildOf(transform) && hits[i].collider.gameObject != gameObject)
                    return transform.InverseTransformPoint(hits[i].point).y;
            }
        }

        // 2) Фолбэк: верх бокса рендерера дороги, ближайшего к StartPoint
        if (road != null && road.startPoint != null && obstacles.Count > 0)
        {
            Renderer best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < obstacles.Count; i++)
            {
                if (obstacles[i] == null) continue;
                float d = (obstacles[i].bounds.center - road.startPoint.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = obstacles[i]; }
            }
            if (best != null)
            {
                Vector3 topWorld = best.bounds.center + Vector3.up * best.bounds.extents.y;
                return transform.InverseTransformPoint(topWorld).y;
            }
        }

        return 0f;
    }

    private float SampleTerrainHeight(float localX, float localZ)
    {
        if (terrainVertices == null) return 0f;

        float u = Mathf.InverseLerp(terrainStartX, terrainEndX, localX) * terrainRes;
        float v = Mathf.InverseLerp(terrainStartZ, terrainEndZ, localZ) * terrainRes;

        int x0 = Mathf.FloorToInt(u);
        int z0 = Mathf.FloorToInt(v);
        x0 = Mathf.Clamp(x0, 0, terrainRes - 1);
        z0 = Mathf.Clamp(z0, 0, terrainRes - 1);
        int x1 = Mathf.Min(x0 + 1, terrainRes);
        int z1 = Mathf.Min(z0 + 1, terrainRes);

        float tx = u - x0;
        float tz = v - z0;

        int row = terrainRes + 1;
        float h00 = terrainVertices[z0 * row + x0].y;
        float h10 = terrainVertices[z0 * row + x1].y;
        float h01 = terrainVertices[z1 * row + x0].y;
        float h11 = terrainVertices[z1 * row + x1].y;

        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    private void GenerateTerrainMesh()
    {
        terrainRes = Mathf.Clamp(resolution, 8, 256);
        if (resolution != terrainRes)
        {
            Debug.LogWarning($"[{name}] resolution {resolution} принудительно ограничен до {terrainRes} (безопасный максимум ChunkTerrain).", this);
            resolution = terrainRes;
        }

        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh mesh = new Mesh { name = "ChunkTerrain" };
        Vector3[] vertices = new Vector3[(terrainRes + 1) * (terrainRes + 1)];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[terrainRes * terrainRes * 6];
        float stepX = width / terrainRes, stepZ = length / terrainRes;
        int vIndex = 0;

        for (int z = 0; z <= terrainRes; z++)
        {
            float localZ = terrainStartZ + z * stepZ;
            for (int x = 0; x <= terrainRes; x++)
            {
                float localX = terrainStartX + x * stepX;
                Vector3 worldPos = transform.TransformPoint(new Vector3(localX, 0f, localZ));

                float dist = distanceField.SampleDistanceLocal(localX, localZ);
                vertices[vIndex] = new Vector3(localX, CalculateHeight(worldPos, localZ, dist), localZ);
                uvs[vIndex] = new Vector2((float)x / terrainRes, (float)z / terrainRes);
                vIndex++;
            }
        }

        int tIndex = 0;
        for (int z = 0; z < terrainRes; z++)
        {
            int row = z * (terrainRes + 1);
            for (int x = 0; x < terrainRes; x++)
            {
                int start = row + x;
                triangles[tIndex++] = start; triangles[tIndex++] = start + terrainRes + 1; triangles[tIndex++] = start + 1;
                triangles[tIndex++] = start + 1; triangles[tIndex++] = start + terrainRes + 1; triangles[tIndex++] = start + terrainRes + 2;
            }
        }

        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices; mesh.triangles = triangles; mesh.uv = uvs;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        terrainVertices = vertices;
        terrainMesh = mesh;
    }

    // Отдельный дешёвый collision mesh (физика != рендер)
    private void BuildTerrainCollisionMesh()
    {
        int colRes = Mathf.Clamp(collisionResolution, 8, 128);
        collisionResolution = colRes;

        Mesh mesh = new Mesh { name = "ChunkTerrainCollision" };
        Vector3[] vertices = new Vector3[(colRes + 1) * (colRes + 1)];
        int[] triangles = new int[colRes * colRes * 6];
        float stepX = width / colRes, stepZ = length / colRes;
        int vIndex = 0;

        for (int z = 0; z <= colRes; z++)
        {
            float localZ = terrainStartZ + z * stepZ;
            for (int x = 0; x <= colRes; x++)
            {
                float localX = terrainStartX + x * stepX;
                Vector3 worldPos = transform.TransformPoint(new Vector3(localX, 0f, localZ));
                float dist = distanceField.SampleDistanceLocal(localX, localZ);
                vertices[vIndex++] = new Vector3(localX, CalculateHeight(worldPos, localZ, dist), localZ);
            }
        }

        int tIndex = 0;
        for (int z = 0; z < colRes; z++)
        {
            int row = z * (colRes + 1);
            for (int x = 0; x < colRes; x++)
            {
                int start = row + x;
                triangles[tIndex++] = start; triangles[tIndex++] = start + colRes + 1; triangles[tIndex++] = start + 1;
                triangles[tIndex++] = start + 1; triangles[tIndex++] = start + colRes + 1; triangles[tIndex++] = start + colRes + 2;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        collisionMesh = mesh;
    }

    private void GenerateEnvironment()
    {
        if (environmentPrefabs == null || environmentPrefabs.Count == 0) return;
        int cellsX = Mathf.FloorToInt(width / environmentGridStep);
        int cellsZ = Mathf.FloorToInt(length / environmentGridStep);
        Transform envContainer = new GameObject("EnvironmentContainer").transform;
        envContainer.SetParent(transform); envContainer.localPosition = Vector3.zero;

        for (int cx = 0; cx < cellsX; cx++)
        {
            for (int cz = 0; cz < cellsZ; cz++)
            {
                EnvironmentObject envObj = environmentPrefabs[Random.Range(0, environmentPrefabs.Count)];
                if (envObj.prefab == null || Random.value > envObj.spawnProbability) continue;

                float x = Mathf.Lerp(terrainStartX, terrainEndX, (cx + Random.value) / cellsX);
                float z = Mathf.Lerp(terrainStartZ, terrainEndZ, (cz + Random.value) / cellsZ);
                Vector3 worldPos = transform.TransformPoint(new Vector3(x, 0f, z));

                float dist = distanceField.SampleDistanceLocal(x, z);
                if (dist < flatZoneRadius + blendZone) continue;

                float terrainY = SampleTerrainHeight(x, z);
                Vector3 finalWorldPos = transform.TransformPoint(new Vector3(x, terrainY, z));
                finalWorldPos.y += envObj.yOffset;

                Vector3 finalNormal = Vector3.up;
                if (envObj.alignToTerrain)
                {
                    float delta = 0.5f;
                    float hL = SampleTerrainHeight(x - delta, z);
                    float hR = SampleTerrainHeight(x + delta, z);
                    float hD = SampleTerrainHeight(x, z - delta);
                    float hU = SampleTerrainHeight(x, z + delta);
                    finalNormal = new Vector3(hL - hR, 2f * delta, hD - hU).normalized;
                }

                GameObject instance = Instantiate(envObj.prefab, finalWorldPos, Quaternion.identity, envContainer);
                Quaternion yaw = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                instance.transform.rotation = envObj.alignToTerrain ? Quaternion.FromToRotation(Vector3.up, finalNormal) * yaw : yaw;
                float scale = Random.Range(envObj.minScale, envObj.maxScale);
                instance.transform.localScale = new Vector3(scale, scale, scale);
            }
        }
    }

    // ================= GRASS: MESHES (3 LOD x N variants) =================

    private void BuildPatchMeshes()
    {
        variantCount = Mathf.Clamp(grassMeshVariants, 1, 4);
        streamCount = LOD_COUNT * variantCount;

        streamMeshes = new Mesh[streamCount];
        streamMatrices = new Matrix4x4[streamCount][];
        streamCounts = new int[streamCount];
        for (int s = 0; s < streamCount; s++)
            streamMatrices[s] = new Matrix4x4[MAX_PER_STREAM];

        for (int v = 0; v < variantCount; v++)
        {
            streamMeshes[v] = BuildPatchMeshLOD0(v);
            streamMeshes[variantCount + v] = BuildPatchMeshLOD1(v);
            streamMeshes[2 * variantCount + v] = BuildPatchMeshLOD2(v);
        }

        patchCullRadius = grassPatchSize * 0.75f + grassMaxScale;
    }

    private Mesh BuildPatchMeshLOD0(int variant)
    {
        int blades = Mathf.Clamp(bladesLOD0, 4, 96);
        Vector3[] verts = new Vector3[blades * 8];
        Vector2[] uvs = new Vector2[blades * 8];
        int[] tris = new int[blades * 12];
        float halfWidth = grassWidth * 0.5f;
        float patchRadius = grassPatchSize * 0.5f;

        for (int i = 0; i < blades; i++)
        {
            float angle = i * 2.399963f + Hash01(i, variant, 101) * 1.2f;
            float radius = Mathf.Sqrt((i + 0.5f) / blades) * patchRadius;
            float px = Mathf.Cos(angle) * radius;
            float pz = Mathf.Sin(angle) * radius;
            float yaw = Hash01(i, variant, 102) * Mathf.PI * 2f;
            float height = 0.85f + 0.35f * Hash01(i, variant, 103);
            WriteCrossedBlade(verts, uvs, tris, i * 8, i * 12, px, pz, yaw, halfWidth, height);
        }
        return CreateMesh("GrassPatch_LOD0_V" + variant, verts, uvs, tris);
    }

    private Mesh BuildPatchMeshLOD1(int variant)
    {
        int blades = Mathf.Clamp(bladesLOD1, 4, 48);
        Vector3[] verts = new Vector3[blades * 4];
        Vector2[] uvs = new Vector2[blades * 4];
        int[] tris = new int[blades * 6];
        float halfWidth = grassWidth * 0.6f; // чуть шире: одна плоскость вместо двух
        float patchRadius = grassPatchSize * 0.5f;

        for (int i = 0; i < blades; i++)
        {
            float angle = i * 2.399963f + Hash01(i, variant, 201) * 1.2f;
            float radius = Mathf.Sqrt((i + 0.5f) / blades) * patchRadius;
            float px = Mathf.Cos(angle) * radius;
            float pz = Mathf.Sin(angle) * radius;
            float yaw = Hash01(i, variant, 202) * Mathf.PI * 2f;
            float height = 0.85f + 0.35f * Hash01(i, variant, 203);

            float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
            Vector3 off = new Vector3(px, 0f, pz);
            Vector3 up = new Vector3(0f, height, 0);
            Vector3 lx = new Vector3(c * halfWidth, 0f, s * halfWidth);
            WriteQuad(verts, uvs, tris, i * 4, i * 6, off - lx, off + lx, off - lx + up, off + lx + up);
        }
        return CreateMesh("GrassPatch_LOD1_V" + variant, verts, uvs, tris);
    }

    private Mesh BuildPatchMeshLOD2(int variant)
    {
        // Tuft: 2 crossed quads, ширина x2.2 — дешёвый силуэт густой травы
        Vector3[] verts = new Vector3[8];
        Vector2[] uvs = new Vector2[8];
        int[] tris = new int[12];
        float halfWidth = grassWidth * 1.1f;
        float yaw = 0.4f + variant * 0.7f;
        WriteCrossedBlade(verts, uvs, tris, 0, 0, 0f, 0f, yaw, halfWidth, 0.9f);
        return CreateMesh("GrassPatch_LOD2_V" + variant, verts, uvs, tris);
    }

    private static void WriteCrossedBlade(Vector3[] verts, Vector2[] uvs, int[] tris, int v, int t,
        float px, float pz, float yaw, float halfWidth, float height)
    {
        float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
        Vector3 off = new Vector3(px, 0f, pz);
        Vector3 up = new Vector3(0f, height, 0);
        Vector3 lx = new Vector3(c * halfWidth, 0f, s * halfWidth);
        Vector3 lz = new Vector3(-s * halfWidth, 0f, c * halfWidth);

        WriteQuad(verts, uvs, tris, v, t, off - lx, off + lx, off - lx + up, off + lx + up);
        WriteQuad(verts, uvs, tris, v + 4, t + 6, off - lz, off + lz, off - lz + up, off + lz + up);
    }

    private static void WriteQuad(Vector3[] verts, Vector2[] uvs, int[] tris, int v, int t,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        verts[v] = a; verts[v + 1] = b; verts[v + 2] = c; verts[v + 3] = d;
        // UV.y: 0 = основание, 1 = вершина (критично для GrassWind)
        uvs[v] = Vector2.zero; uvs[v + 1] = Vector2.right; uvs[v + 2] = Vector2.up; uvs[v + 3] = Vector2.one;
        tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
        tris[t + 3] = v + 2; tris[t + 4] = v + 1; tris[t + 5] = v + 3;
    }

    private static Mesh CreateMesh(string meshName, Vector3[] verts, Vector2[] uvs, int[] tris)
    {
        Mesh mesh = new Mesh { name = meshName };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals(); // <--- Обязательно добавь эту строчку
        return mesh;
    }

    // ================= GRASS: PATCH GRID =================

    private void GenerateGrassPatches()
    {
        if (grassMaterial == null) { patches = null; return; }
        if (grassPatchSize <= 0.1f) grassPatchSize = 4f;

        patchGridX = Mathf.Max(1, Mathf.FloorToInt(width / grassPatchSize));
        patchGridZ = Mathf.Max(1, Mathf.FloorToInt(length / grassPatchSize));
        patches = new GrassPatch[patchGridX * patchGridZ];

        for (int pz = 0; pz < patchGridZ; pz++)
        {
            for (int px = 0; px < patchGridX; px++)
            {
                float x = terrainStartX + (px + 0.5f) * grassPatchSize;
                float z = terrainStartZ + (pz + 0.5f) * grassPatchSize;

                float dist = distanceField.SampleDistanceLocal(x, z);
                if (dist < grassRoadMargin) continue;

                float density;
                if (dist < grassRoadMargin + grassTransitionZone)
                {
                    float t = Mathf.InverseLerp(grassRoadMargin, grassRoadMargin + grassTransitionZone, dist);
                    density = Mathf.Lerp(grassNearRoadDensity, grassDensity, Mathf.SmoothStep(0f, 1f, t));
                }
                else density = grassDensity;

                if (Hash01(px, pz, 11) > density) continue;

                GrassPatch p = new GrassPatch();
                float terrainY = SampleTerrainHeight(x, z);
                p.worldPos = transform.TransformPoint(new Vector3(x, terrainY, z));
                float yaw = Hash01(px, pz, 23) * Mathf.PI * 2f;
                p.rotC = Mathf.Cos(yaw);
                p.rotS = Mathf.Sin(yaw);
                p.scale = Mathf.Lerp(grassMinScale, grassMaxScale, Hash01(px, pz, 37));
                p.rand01 = Hash01(px, pz, 53);
                p.variant = (byte)Mathf.Min(variantCount - 1, (int)(Hash01(px, pz, 71) * variantCount));
                p.alive = true;
                patches[pz * patchGridX + px] = p;
            }
        }
        grassDirty = true;
    }

    private static float Hash01(int x, int z, int salt)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)z * 668265263u + (uint)salt * 974634321u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (float)(h / 4294967295.0);
        }
    }

    // ================= GRASS: REBUILD (единственное место, где считаются матрицы) =================

    private void RebuildVisiblePatches(Vector3 camPos, Quaternion camRot)
    {
        for (int s = 0; s < streamCount; s++) streamCounts[s] = 0;
        if (patches == null) return;

        GeometryUtility.CalculateFrustumPlanes(cachedCamera, frustumPlanes);

        Vector3 camForward = camRot * Vector3.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude > 0.001f) camForward.Normalize();
        else camForward = Vector3.forward;

        Vector3 cullCenter = camPos + camForward * (grassRenderDistance * 0.15f);

        float lod0Sqr = grassLOD0Distance * grassLOD0Distance;
        float lod1Sqr = grassLOD1Distance * grassLOD1Distance;
        float lod2Sqr = grassLOD2Distance * grassLOD2Distance;
        float renderSqr = grassRenderDistance * grassRenderDistance;

        Vector3 localCenter = transform.InverseTransformPoint(cullCenter);
        float search = grassRenderDistance + grassPatchSize;
        int minPx = Mathf.Clamp(Mathf.FloorToInt((localCenter.x - search - terrainStartX) / grassPatchSize), 0, patchGridX - 1);
        int maxPx = Mathf.Clamp(Mathf.CeilToInt((localCenter.x + search - terrainStartX) / grassPatchSize), 0, patchGridX - 1);
        int minPz = Mathf.Clamp(Mathf.FloorToInt((localCenter.z - search - terrainStartZ) / grassPatchSize), 0, patchGridZ - 1);
        int maxPz = Mathf.Clamp(Mathf.CeilToInt((localCenter.z + search - terrainStartZ) / grassPatchSize), 0, patchGridZ - 1);

        for (int pz = minPz; pz <= maxPz; pz++)
        {
            int rowBase = pz * patchGridX;
            for (int px = minPx; px <= maxPx; px++)
            {
                GrassPatch p = patches[rowBase + px];
                if (!p.alive) continue;

                float dx = p.worldPos.x - cullCenter.x;
                float dz = p.worldPos.z - cullCenter.z;
                float distSqr = dx * dx + dz * dz;
                if (distSqr > renderSqr) continue;

                int lod;
                float density;
                if (distSqr <= lod0Sqr) { lod = 0; density = lod0Density; }
                else if (distSqr <= lod1Sqr) { lod = 1; density = lod1Density; }
                else if (distSqr <= lod2Sqr) { lod = 2; density = lod2Density; }
                else continue;

                // LOD прореживает КОЛИЧЕСТВО инстансов, а не только полигонаж
                if (density < 1f && p.rand01 > density) continue;

                // Frustum culling на уровне patch (sphere vs 6 planes)
                if (!PatchInFrustum(p.worldPos, patchCullRadius)) continue;

                int stream = lod * variantCount + p.variant;
                int n = streamCounts[stream];
                if (n >= MAX_PER_STREAM) continue;

                float cs = p.rotC * p.scale;
                float sn = p.rotS * p.scale;
                Matrix4x4 m = new Matrix4x4();
                m.m00 = cs; m.m02 = sn; m.m03 = p.worldPos.x;
                m.m11 = p.scale; m.m13 = p.worldPos.y;
                m.m20 = -sn; m.m22 = cs; m.m23 = p.worldPos.z;
                m.m33 = 1f;
                streamMatrices[stream][n] = m;
                streamCounts[stream] = n + 1;
            }
        }
    }

    private bool PatchInFrustum(Vector3 p, float r)
    {
        for (int i = 0; i < 6; i++)
        {
            Plane pl = frustumPlanes[i];
            if (pl.normal.x * p.x + pl.normal.y * p.y + pl.normal.z * p.z + pl.distance < -r)
                return false;
        }
        return true;
    }

    // ================= GRASS: DRAW (каждый кадр, 0 вычислений) =================

    private void DrawGrass()
    {
        if (grassMaterial == null || streamMeshes == null) return;

        for (int s = 0; s < streamCount; s++)
        {
            int count = streamCounts[s];
            if (count == 0) continue;
            Mesh mesh = streamMeshes[s];
            if (mesh == null) continue;

            // Тень кастует только ближний LOD (или сколько разрешил shadowMaxLOD)
            int lod = s / variantCount;
            UnityEngine.Rendering.ShadowCastingMode cast =
                (grassCastShadows && lod <= shadowMaxLOD)
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;

            Matrix4x4[] buffer = streamMatrices[s];
            int index = 0;
            while (index < count)
            {
                int batch = Mathf.Min(BATCH_SIZE, count - index);
                System.Array.Copy(buffer, index, batchMatrices, 0, batch);
                Graphics.DrawMeshInstanced(
                    mesh, 0, grassMaterial,
                    batchMatrices, batch, null,
                    cast,
                    true,   // receiveShadows: трава принимает тень машины
                    0, cachedCamera);
                index += batch;
            }
        }
    }

    // ================= UPDATE =================

    void Update()
    {
        if (!isInitialized || grassMaterial == null || streamMeshes == null) return;

        if (cachedCamera == null)
        {
            cachedCamera = Camera.main;
            if (cachedCamera == null) return;
            grassDirty = true;
        }

        Vector3 camPos = cachedCamera.transform.position;
        Quaternion camRot = cachedCamera.transform.rotation;

        Vector3 delta = camPos - lastRebuildPosition;
        delta.y = 0f;

        if (grassDirty || delta.sqrMagnitude > rebuildThresholdSqr || Quaternion.Angle(camRot, lastCameraRot) > 2f)
        {
            grassUpdateTimer++;
            if (grassUpdateTimer >= Mathf.Max(1, maxGrassUpdateInterval))
            {
                grassUpdateTimer = 0;
                grassDirty = false;
                lastCameraRot = camRot;
                lastRebuildPosition = new Vector3(camPos.x, 0f, camPos.z);
                RebuildVisiblePatches(camPos, camRot);
            }
        }

        DrawGrass();
    }

    // ================= DISTANCE FIELD (сохранён) =================

    public class DistanceField
    {
        public float[] distances;
        public int gridSize;
        private float startX, endX, startZ, endZ, inverseWidth, inverseLength;
        private Transform chunkTransform;

        public DistanceField(List<Renderer> obstacles, float sX, float eX, float sZ, float eZ, int gridRes, Transform trans)
        {
            startX = sX; endX = eX; startZ = sZ; endZ = eZ; gridSize = gridRes; chunkTransform = trans;
            inverseWidth = 1f / Mathf.Max(0.0001f, endX - startX);
            inverseLength = 1f / Mathf.Max(0.0001f, endZ - startZ);
            distances = new float[gridSize * gridSize];

            if (obstacles == null || obstacles.Count == 0)
            {
                for (int i = 0; i < distances.Length; i++) distances[i] = 100000f;
                return;
            }

            float stepX = (endX - startX) / (gridSize - 1);
            float stepZ = (endZ - startZ) / (gridSize - 1);

            for (int z = 0; z < gridSize; z++)
            {
                float localZ = startZ + z * stepZ;
                for (int x = 0; x < gridSize; x++)
                {
                    float localX = startX + x * stepX;
                    Vector3 worldPos = chunkTransform.TransformPoint(new Vector3(localX, 0f, localZ));
                    float minDistSqr = float.MaxValue;
                    for (int i = 0; i < obstacles.Count; i++) { if (obstacles[i] == null) continue; float sqrDist = obstacles[i].bounds.SqrDistance(worldPos); if (sqrDist < minDistSqr) minDistSqr = sqrDist; }
                    distances[z * gridSize + x] = Mathf.Sqrt(minDistSqr);
                }
            }
        }

        public float SampleDistanceLocal(float localX, float localZ)
        {
            float u = Mathf.Clamp01((localX - startX) * inverseWidth);
            float v = Mathf.Clamp01((localZ - startZ) * inverseLength);
            float fx = u * (gridSize - 1), fz = v * (gridSize - 1);
            int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
            int x1 = Mathf.Min(x0 + 1, gridSize - 1), z1 = Mathf.Min(z0 + 1, gridSize - 1);
            float tx = fx - x0, tz = fz - z0;
            float d0 = Mathf.Lerp(distances[z0 * gridSize + x0], distances[z0 * gridSize + x1], tx);
            float d1 = Mathf.Lerp(distances[z1 * gridSize + x0], distances[z1 * gridSize + x1], tx);
            return Mathf.Lerp(d0, d1, tz);
        }

        public float SampleDistance(Vector3 worldPoint)
        {
            Vector3 local = chunkTransform.InverseTransformPoint(worldPoint);
            return SampleDistanceLocal(local.x, local.z);
        }
    }
}