using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ChunkGroundGenerator : MonoBehaviour
{
    [Header("Ландшафт")]
    public int resolution = 32;
    public float heightScale = 10f;
    public float noiseScale = 0.02f;

    [Header("Препятствия (дороги, объекты)")]
    public float flatZoneRadius = 8f;
    public float blendZone = 15f;
    public float edgeBlendZone = 20f;
    public float terrainPadding = 40f;

    [Header("Трава")]
    public Material grassMaterial;
    public float grassStep = 1.0f;
    public float grassMinScale = 0.7f;
    public float grassMaxScale = 1.3f;
    public float grassWidth = 0.4f;

    [Header("Оптимизация травы")]
    public float renderDistance = 80f;
    public float grassCellSize = 10f;
    public int maxGrassUpdateInterval = 2;

    [Header("LOD (Спасение GPU от overdraw)")]
    public float lodDistance = 40f;

    [Header("Окружение (Деревья, камни)")]
    public float environmentGridStep = 12f;
    public List<EnvironmentObject> environmentPrefabs;

    [Header("Густота и зоны травы")]
    [Range(0.1f, 1f)] public float grassDensity = 0.8f;
    public float grassRoadMargin = 2.5f;
    public float grassTransitionZone = 8f;
    [Range(0f, 1f)] public float grassNearRoadDensity = 0.2f;

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

    private float terrainStartX, terrainEndX, terrainStartZ, terrainEndZ;
    private float width, length;
    private float perlinOffsetX, perlinOffsetZ;

    private Camera cachedCamera;
    private Vector3 lastCameraPos;
    private Quaternion lastCameraRot;
    private Plane[] frustumPlanes = new Plane[6];
    private Bounds cellBounds;

    private Mesh grassMesh;
    private Mesh terrainMesh; // FIX: Сохраняем для OnDestroy
    private Vector3[] terrainVertices; // FIX: Для билинейной интерполяции высот

    private struct GrassData
    {
        public Vector3 worldPos;
        public float rotY;
        public float scale;
    }

    private readonly Dictionary<Vector2Int, List<GrassData>> grassCells = new Dictionary<Vector2Int, List<GrassData>>();
    private readonly List<GrassData> visibleGrass = new List<GrassData>(65536); // FIX: Увеличен capacity
    private readonly Matrix4x4[] batchMatrices = new Matrix4x4[1023];

    private int grassUpdateTimer = 0;
    private bool grassDirty = true;

    private Vector3 lastRebuildPosition;
    private float rebuildThresholdSqr;
    private bool isInitialized = false;

    void Start()
    {
        cachedCamera = Camera.main;

        perlinOffsetX = transform.position.x * 0.13f + 1000f;
        perlinOffsetZ = transform.position.z * 0.17f + 1000f;

        float rebuildThreshold = grassCellSize * 0.4f;
        rebuildThresholdSqr = rebuildThreshold * rebuildThreshold;
        lastRebuildPosition = Vector3.zero;

        cellBounds = new Bounds(Vector3.zero, new Vector3(grassCellSize, heightScale + grassMaxScale * 2f, grassCellSize));

        grassUpdateTimer = maxGrassUpdateInterval;
        if (cachedCamera != null)
        {
            lastCameraPos = cachedCamera.transform.position;
            lastCameraRot = cachedCamera.transform.rotation;
        }

        if (!isInitialized) InitChunk();
    }

    void OnDestroy()
    {
        if (grassMesh != null)
            Destroy(grassMesh);
        if (terrainMesh != null) // FIX: Уничтожаем terrain mesh
            Destroy(terrainMesh);
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
        GenerateTerrainMesh();
        GenerateEnvironment();
        PrepareGrassMesh();
        GenerateGrassCells();

        isInitialized = true;
        grassDirty = true;
    }

    private float CalculateHeight(Vector3 worldPos, float localZ, float dist)
    {
        if (dist <= flatZoneRadius) return 0f;
        float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flatZoneRadius, flatZoneRadius + blendZone, dist));
        float distToEdgeZ = Mathf.Min(localZ - terrainStartZ, terrainEndZ - localZ);
        fade *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, edgeBlendZone, distToEdgeZ));
        return Mathf.PerlinNoise((worldPos.x + perlinOffsetX) * noiseScale, (worldPos.z + perlinOffsetZ) * noiseScale) * heightScale * fade;
    }

    // FIX: Билинейная интерполяция из фактических вершин terrain'а
    private float SampleTerrainHeight(float localX, float localZ)
    {
        if (terrainVertices == null) return 0f;

        float u = Mathf.InverseLerp(terrainStartX, terrainEndX, localX) * resolution;
        float v = Mathf.InverseLerp(terrainStartZ, terrainEndZ, localZ) * resolution;

        int x0 = Mathf.FloorToInt(u);
        int z0 = Mathf.FloorToInt(v);
        x0 = Mathf.Clamp(x0, 0, resolution - 1);
        z0 = Mathf.Clamp(z0, 0, resolution - 1);
        int x1 = Mathf.Min(x0 + 1, resolution);
        int z1 = Mathf.Min(z0 + 1, resolution);

        float tx = u - x0;
        float tz = v - z0;

        int row = resolution + 1;
        float h00 = terrainVertices[z0 * row + x0].y;
        float h10 = terrainVertices[z0 * row + x1].y;
        float h01 = terrainVertices[z1 * row + x0].y;
        float h11 = terrainVertices[z1 * row + x1].y;

        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    private void GenerateTerrainMesh()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh mesh = new Mesh { name = "ChunkTerrain" };
        Vector3[] vertices = new Vector3[(resolution + 1) * (resolution + 1)];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[resolution * resolution * 6];
        float stepX = width / resolution, stepZ = length / resolution;
        int vIndex = 0;

        for (int z = 0; z <= resolution; z++)
        {
            float localZ = terrainStartZ + z * stepZ;
            for (int x = 0; x <= resolution; x++)
            {
                float localX = terrainStartX + x * stepX;
                Vector3 worldPos = transform.TransformPoint(new Vector3(localX, 0f, localZ));

                float dist = distanceField.SampleDistanceLocal(localX, localZ);
                vertices[vIndex] = new Vector3(localX, CalculateHeight(worldPos, localZ, dist), localZ);
                uvs[vIndex] = new Vector2((float)x / resolution, (float)z / resolution);
                vIndex++;
            }
        }

        int tIndex = 0;
        for (int z = 0; z < resolution; z++)
        {
            int row = z * (resolution + 1);
            for (int x = 0; x < resolution; x++)
            {
                int start = row + x;
                triangles[tIndex++] = start; triangles[tIndex++] = start + resolution + 1; triangles[tIndex++] = start + 1;
                triangles[tIndex++] = start + 1; triangles[tIndex++] = start + resolution + 1; triangles[tIndex++] = start + resolution + 2;
            }
        }

        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices; mesh.triangles = triangles; mesh.uv = uvs;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        // FIX: Сохраняем для билинейной интерполяции и OnDestroy
        terrainVertices = vertices;
        terrainMesh = mesh;

        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
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

                // FIX: Используем билинейную интерполяцию вместо Raycast (быстрее в 100 раз)
                float terrainY = SampleTerrainHeight(x, z);
                Vector3 finalWorldPos = transform.TransformPoint(new Vector3(x, terrainY, z));
                finalWorldPos.y += envObj.yOffset;

                // Нормаль аппроксимируем из соседних высот
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

    private void PrepareGrassMesh()
    {
        grassMesh = new Mesh { name = "GrassBlade" };
        Vector3[] vertices = new Vector3[8]; Vector2[] uv = new Vector2[8]; int[] triangles = new int[12];

        float halfWidth = 0.5f;
        float height = 1f;

        vertices[0] = new Vector3(-halfWidth, 0f, 0f); vertices[1] = new Vector3(halfWidth, 0f, 0f);
        vertices[2] = new Vector3(-halfWidth, height, 0f); vertices[3] = new Vector3(halfWidth, height, 0f);
        vertices[4] = new Vector3(0f, 0f, -halfWidth); vertices[5] = new Vector3(0f, 0f, halfWidth);
        vertices[6] = new Vector3(0f, height, -halfWidth); vertices[7] = new Vector3(0f, height, halfWidth);

        for (int i = 0; i < 2; i++) { int idx = i * 4; uv[idx] = new Vector2(0, 0); uv[idx + 1] = new Vector2(1, 0); uv[idx + 2] = new Vector2(0, 1); uv[idx + 3] = new Vector2(1, 1); }
        triangles[0] = 0; triangles[1] = 1; triangles[2] = 2; triangles[3] = 2; triangles[4] = 1; triangles[5] = 3;
        triangles[6] = 4; triangles[7] = 5; triangles[8] = 6; triangles[9] = 6; triangles[10] = 5; triangles[11] = 7;

        grassMesh.vertices = vertices; grassMesh.uv = uv; grassMesh.triangles = triangles;
        grassMesh.RecalculateNormals(); grassMesh.RecalculateBounds();
    }

    private void GenerateGrassCells()
    {
        grassCells.Clear(); visibleGrass.Clear();
        if (grassMaterial == null) return;
        if (grassStep <= 0.01f) grassStep = 1f;
        if (grassCellSize <= 0.01f) grassCellSize = 10f;

        int cellsX = Mathf.CeilToInt(width / grassStep);
        int cellsZ = Mathf.CeilToInt(length / grassStep);

        for (int cx = 0; cx < cellsX; cx++)
        {
            for (int cz = 0; cz < cellsZ; cz++)
            {
                float x = terrainStartX + (cx + Random.value) * grassStep;
                float z = terrainStartZ + (cz + Random.value) * grassStep;

                // FIX: Убран избыточный Clamp (CeilToInt гарантирует покрытие)

                Vector3 localPos = new Vector3(x, 0f, z);
                Vector3 worldPos = transform.TransformPoint(localPos);

                float dist = distanceField.SampleDistanceLocal(x, z);

                if (dist < grassRoadMargin) continue;

                float currentDensity = dist < grassRoadMargin + grassTransitionZone
                    ? Mathf.Lerp(grassNearRoadDensity, grassDensity, Mathf.SmoothStep(0f, 1f, (dist - grassRoadMargin) / grassTransitionZone))
                    : grassDensity;

                if (Random.value > currentDensity) continue;

                // FIX: Билинейная интерполяция вместо CalculateHeight (трава идеально прилегает к мешу)
                localPos.y = SampleTerrainHeight(x, z);
                worldPos = transform.TransformPoint(localPos);

                Vector2Int cell = GetGrassCell(worldPos);
                if (!grassCells.TryGetValue(cell, out List<GrassData> list))
                { list = new List<GrassData>(64); grassCells.Add(cell, list); }

                list.Add(new GrassData
                {
                    worldPos = worldPos,
                    rotY = Random.Range(0f, 360f),
                    scale = Random.Range(grassMinScale, grassMaxScale)
                });
            }
        }
        grassDirty = true;
    }

    private Vector2Int GetGrassCell(Vector3 worldPosition) => new Vector2Int(Mathf.FloorToInt(worldPosition.x / grassCellSize), Mathf.FloorToInt(worldPosition.z / grassCellSize));

    private void Update()
    {
        if (!isInitialized || grassMesh == null || grassMaterial == null) return;

        // FIX: Пересчет frustum planes при смене/восстановлении камеры
        if (cachedCamera == null)
        {
            cachedCamera = Camera.main;
            if (cachedCamera != null)
            {
                GeometryUtility.CalculateFrustumPlanes(cachedCamera, frustumPlanes);
                lastCameraPos = cachedCamera.transform.position;
                lastCameraRot = cachedCamera.transform.rotation;
                grassDirty = true;
            }
            return;
        }

        Vector3 camPos = cachedCamera.transform.position;
        Quaternion camRot = cachedCamera.transform.rotation;

        Vector3 deltaPos = camPos - lastRebuildPosition;
        deltaPos.y = 0f;

        // FIX: Поднят порог чувствительности с 0.5f до 2.0f (меньше дребезга от тряски)
        bool rotated = Quaternion.Angle(camRot, lastCameraRot) > 2.0f;

        if (grassDirty || deltaPos.sqrMagnitude > rebuildThresholdSqr || rotated)
        {
            grassDirty = true;
        }

        grassUpdateTimer++;
        if (grassUpdateTimer >= Mathf.Max(1, maxGrassUpdateInterval))
        {
            grassUpdateTimer = 0;
            if (grassDirty)
            {
                RebuildVisibleGrass(camPos, camRot);
                lastRebuildPosition = new Vector3(camPos.x, 0f, camPos.z);
            }
        }

        DrawVisibleGrass();
    }

    private void RebuildVisibleGrass(Vector3 camPos, Quaternion camRot)
    {
        grassDirty = false;
        visibleGrass.Clear();

        bool cameraMoved = (camPos - lastCameraPos).sqrMagnitude > 0.0001f;
        bool cameraRotated = Quaternion.Angle(camRot, lastCameraRot) > 0.01f;

        if (cameraMoved || cameraRotated)
        {
            GeometryUtility.CalculateFrustumPlanes(cachedCamera, frustumPlanes);
            lastCameraPos = camPos;
            lastCameraRot = camRot;
        }

        float distanceSqr = renderDistance * renderDistance;
        float lodDistanceSqr = lodDistance * lodDistance;

        // 🔥 FIX (Kimi/DeepSeek): Асимметричный culling для гонок/езды
        // 1. Получаем направление "вперёд" камеры (игнорируя наклон по Y)
        Vector3 camForward = camRot * Vector3.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude > 0.001f) camForward.Normalize();
        else camForward = Vector3.forward;

        // 2. Сдвигаем центр проверки дистанции вперед на 40% от renderDistance
        float shiftAmount = renderDistance * 0.4f;
        Vector3 cullCenter = camPos + camForward * shiftAmount;

        // 3. Ищем клетки вокруг СДВИНУТОГО центра
        Vector2Int cullCell = GetGrassCell(cullCenter);

        // Радиус поиска должен покрывать сдвинутую сферу (R + 0.4R = 1.4R)
        float searchRadius = renderDistance * 1.45f;
        int cellRadius = Mathf.CeilToInt(searchRadius / grassCellSize);

        float maxCellDist = searchRadius + grassCellSize * 0.75f;
        float maxCellDistSqr = maxCellDist * maxCellDist;

        float chunkY = transform.position.y;

        for (int x = -cellRadius; x <= cellRadius; x++)
        {
            for (int z = -cellRadius; z <= cellRadius; z++)
            {
                Vector2Int cellCoord = new Vector2Int(cullCell.x + x, cullCell.y + z);
                float cx = (cellCoord.x + 0.5f) * grassCellSize;
                float cz = (cellCoord.y + 0.5f) * grassCellSize;

                // Расстояние от клетки до СДВИНУТОГО центра
                float dx = cx - cullCenter.x;
                float dz = cz - cullCenter.z;

                if (dx * dx + dz * dz > maxCellDistSqr) continue;

                cellBounds.center = new Vector3(cx, chunkY + heightScale * 0.5f, cz);
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, cellBounds)) continue;

                if (!grassCells.TryGetValue(cellCoord, out List<GrassData> list)) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    GrassData grass = list[i];

                    // Расстояние от травы до СДВИНУТОГО центра
                    float dxg = grass.worldPos.x - cullCenter.x;
                    float dzg = grass.worldPos.z - cullCenter.z;
                    float distSqr = dxg * dxg + dzg * dzg;

                    if (distSqr <= distanceSqr)
                    {
                        if (distSqr > lodDistanceSqr)
                        {
                            int hash = Mathf.FloorToInt(grass.worldPos.x * 7.3f + grass.worldPos.z * 11.7f);
                            if ((hash & 1) == 0) continue;
                        }
                        visibleGrass.Add(grass);
                    }
                }
            }
        }
    }

    private void DrawVisibleGrass()
    {
        int count = visibleGrass.Count;
        if (count == 0) return;

        int index = 0;
        while (index < count)
        {
            int batchCount = Mathf.Min(1023, count - index);

            for (int i = 0; i < batchCount; i++)
            {
                GrassData data = visibleGrass[index + i];

                float rad = data.rotY * Mathf.Deg2Rad;
                float c = Mathf.Cos(rad);
                float s = Mathf.Sin(rad);

                float sx = grassWidth;
                float sy = data.scale;
                float sz = grassWidth;

                Matrix4x4 m = new Matrix4x4();
                m.m00 = c * sx; m.m01 = 0; m.m02 = s * sz; m.m03 = data.worldPos.x;
                m.m10 = 0; m.m11 = sy; m.m12 = 0; m.m13 = data.worldPos.y;
                m.m20 = -s * sx; m.m21 = 0; m.m22 = c * sz; m.m23 = data.worldPos.z;
                m.m30 = 0; m.m31 = 0; m.m32 = 0; m.m33 = 1f;

                batchMatrices[i] = m;
            }

            Graphics.DrawMeshInstanced(
                grassMesh, 0, grassMaterial,
                batchMatrices, batchCount, null,
                UnityEngine.Rendering.ShadowCastingMode.Off,
                false,
                0,
                cachedCamera
            );

            index += batchCount;
        }
    }

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