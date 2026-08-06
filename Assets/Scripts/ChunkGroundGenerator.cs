using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ChunkGroundGenerator : MonoBehaviour
{
    [Header("Ландшафт")]
    public int resolution = 32;
    public float heightScale = 20f;
    public float noiseScale = 0.02f;

    [Header("Препятствия (дороги, объекты)")]
    public float flatZoneRadius = 8f;
    public float blendZone = 15f;
    public float edgeBlendZone = 20f;

    [Tooltip("Сколько добавить пустого места (обочины) по краям дороги")]
    public float terrainPadding = 40f;

    [Header("Трава (GPU Instancing)")]
    public Mesh grassQuadMesh;
    public Material grassMaterial;
    public float grassStep = 3f;
    public float grassMinScale = 0.6f;
    public float grassMaxScale = 1.4f;
    public int maxGrassPoints = 30000;

    [Header("Окружение (Деревья, камни)")]
    public float environmentGridStep = 10f;
    public List<EnvironmentObject> environmentPrefabs;

    [System.Serializable]
    public class EnvironmentObject
    {
        public GameObject prefab;
        [Range(0f, 1f)] public float spawnProbability = 0.1f;
        public float minScale = 0.8f;
        public float maxScale = 1.5f;
        public bool alignToTerrain = false;
        public float yOffset = -0.1f;
    }

    private DistanceField distanceField;
    private List<Vector3> grassPositions = new List<Vector3>();
    private List<Vector3> grassNormals = new List<Vector3>();
    private Matrix4x4[][] grassBatches;
    private bool grassReady = false;

    // Динамические границы чанка (вычисляются автоматически)
    private float terrainStartX, terrainEndX, terrainStartZ, terrainEndZ;
    private float width, length;

    private float perlinOffsetX = 10000f;
    private float perlinOffsetZ = 10000f;

    public void InitChunk()
    {
        RoadChunk road = GetComponent<RoadChunk>();
        List<Renderer> obstacles = new List<Renderer>();

        // 1. ВЫЧИСЛЕНИЕ РЕАЛЬНЫХ ГРАНИЦ ДОРОГИ
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        Renderer[] childRenderers = GetComponentsInChildren<Renderer>();
        foreach (var r in childRenderers)
        {
            if (r.gameObject == gameObject) continue;

            // Игнорируем точки старта/конца, если на них есть рендереры
            if (road != null)
            {
                if (road.startPoint != null && r.gameObject == road.startPoint.gameObject) continue;
                if (road.endPoint != null && r.gameObject == road.endPoint.gameObject) continue;
            }

            obstacles.Add(r);

            // Преобразуем мировые bounds в локальные координаты чанка
            Vector3 localMin = transform.InverseTransformPoint(r.bounds.min);
            Vector3 localMax = transform.InverseTransformPoint(r.bounds.max);

            minX = Mathf.Min(minX, localMin.x);
            maxX = Mathf.Max(maxX, localMax.x);
            minZ = Mathf.Min(minZ, localMin.z);
            maxZ = Mathf.Max(maxZ, localMax.z);
        }

        // Если коллайдеров нет, ставим запасные значения
        if (minX == float.MaxValue) { minX = -20; maxX = 20; minZ = 0; maxZ = 50; }

        // 2. РАСШИРЯЕМ ГРАНИЦЫ ДЛЯ ОБЛОЖИНЫ
        terrainStartX = minX - terrainPadding;
        terrainEndX = maxX + terrainPadding;
        terrainStartZ = minZ;
        terrainEndZ = maxZ;

        width = terrainEndX - terrainStartX;
        length = terrainEndZ - terrainStartZ;

        if (grassQuadMesh == null) grassQuadMesh = CreateGrassQuadMesh();

        // 3. Инициализация поля расстояний с новыми границами
        distanceField = new DistanceField(obstacles, terrainStartX, terrainEndX, terrainStartZ, terrainEndZ, 64, transform);

        GenerateTerrainMesh();
        GenerateGrassPoints();
        GenerateEnvironment();

        grassReady = true;
        if (grassMaterial != null) grassMaterial.enableInstancing = true;
    }

    void Update()
    {
        if (grassReady && grassBatches != null && grassMaterial != null && grassMaterial.enableInstancing)
        {
            foreach (var batch in grassBatches)
            {
                if (batch.Length > 0)
                {
                    Graphics.DrawMeshInstanced(grassQuadMesh, 0, grassMaterial, batch, batch.Length, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
                }
            }
        }
    }

    private float CalculateHeight(Vector3 worldPos, float localZ, float dist)
    {
        if (dist <= flatZoneRadius)
            return 0f;

        float fade = Mathf.InverseLerp(flatZoneRadius, flatZoneRadius + blendZone, dist);
        fade = Mathf.SmoothStep(0f, 1f, fade);

        // Сглаживаем края по Z (чтобы чанки стыковались без швов)
        float distToEdgeZ = Mathf.Min(localZ - terrainStartZ, terrainEndZ - localZ);
        float edgeFade = Mathf.InverseLerp(0f, edgeBlendZone, distToEdgeZ);
        fade *= Mathf.SmoothStep(0f, 1f, edgeFade);

        float noiseX = (worldPos.x + perlinOffsetX) * noiseScale;
        float noiseZ = (worldPos.z + perlinOffsetZ) * noiseScale;

        return Mathf.PerlinNoise(noiseX, noiseZ) * heightScale * fade;
    }

    Mesh CreateGrassQuadMesh()
    {
        Mesh mesh = new Mesh { name = "GrassQuad" };
        Vector3[] vertices = new Vector3[8];
        Vector2[] uv = new Vector2[8];
        int[] triangles = new int[12];

        float hw = 0.5f, h = 1.0f;
        vertices[0] = new Vector3(-hw, 0, 0); vertices[1] = new Vector3(hw, 0, 0);
        vertices[2] = new Vector3(-hw, h, 0); vertices[3] = new Vector3(hw, h, 0);
        vertices[4] = new Vector3(0, 0, -hw); vertices[5] = new Vector3(0, 0, hw);
        vertices[6] = new Vector3(0, h, -hw); vertices[7] = new Vector3(0, h, hw);

        uv[0] = new Vector2(0, 0); uv[1] = new Vector2(1, 0); uv[2] = new Vector2(0, 1); uv[3] = new Vector2(1, 1);
        uv[4] = new Vector2(0, 0); uv[5] = new Vector2(1, 0); uv[6] = new Vector2(0, 1); uv[7] = new Vector2(1, 1);

        triangles[0] = 0; triangles[1] = 1; triangles[2] = 2; triangles[3] = 2; triangles[4] = 1; triangles[5] = 3;
        triangles[6] = 4; triangles[7] = 5; triangles[8] = 6; triangles[9] = 6; triangles[10] = 5; triangles[11] = 7;

        mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(10000f, 10000f, 10000f));
        return mesh;
    }

    void GenerateTerrainMesh()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();

        int rezX = resolution;
        int rezZ = resolution;

        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[(rezX + 1) * (rezZ + 1)];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[rezX * rezZ * 6];

        float stepX = width / rezX;
        float stepZ = length / rezZ;

        int vIndex = 0;
        for (int z = 0; z <= rezZ; z++)
        {
            for (int x = 0; x <= rezX; x++)
            {
                float localX = terrainStartX + (x * stepX);
                float localZ = terrainStartZ + (z * stepZ);

                Vector3 worldPos = transform.TransformPoint(new Vector3(localX, 0, localZ));
                float dist = distanceField.SampleDistance(worldPos);

                // Используем правильный расчет высоты с шумом Перлина
                float height = CalculateHeight(worldPos, localZ, dist);

                vertices[vIndex] = new Vector3(localX, height, localZ);
                uvs[vIndex] = new Vector2((float)x / rezX, (float)z / rezZ);
                vIndex++;
            }
        }

        int tIndex = 0;
        for (int z = 0; z < rezZ; z++)
        {
            for (int x = 0; x < rezX; x++)
            {
                int start = z * (rezX + 1) + x;
                triangles[tIndex++] = start;
                triangles[tIndex++] = start + rezX + 1;
                triangles[tIndex++] = start + 1;

                triangles[tIndex++] = start + 1;
                triangles[tIndex++] = start + rezX + 1;
                triangles[tIndex++] = start + rezX + 2;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mf.mesh = mesh;

        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
    }

    void GenerateGrassPoints()
    {
        grassPositions.Clear();
        grassNormals.Clear();

        int cellsX = Mathf.FloorToInt(width / grassStep);
        int cellsZ = Mathf.FloorToInt(length / grassStep);

        MeshCollider chunkCollider = GetComponent<MeshCollider>();

        for (int cx = 0; cx < cellsX; cx++)
        {
            for (int cz = 0; cz < cellsZ; cz++)
            {
                if (grassPositions.Count >= maxGrassPoints) break;

                float x = Mathf.Lerp(terrainStartX, terrainEndX, (cx + Random.value) / cellsX);
                float z = Mathf.Lerp(terrainStartZ, terrainEndZ, (cz + Random.value) / cellsZ);

                Vector3 localPos = new Vector3(x, 0, z);
                Vector3 worldPos = transform.TransformPoint(localPos);
                float dist = distanceField.SampleDistance(worldPos);

                if (dist < flatZoneRadius + 1.0f) continue;

                Vector3 finalWorldPos = worldPos;
                Vector3 finalNormal = Vector3.up;
                bool hitFound = false;

                if (chunkCollider != null && chunkCollider.sharedMesh != null)
                {
                    Vector3 rayStart = worldPos + Vector3.up * (heightScale * 2f + 100f);
                    if (chunkCollider.Raycast(new Ray(rayStart, Vector3.down), out RaycastHit hit, heightScale * 5f + 200f))
                    {
                        finalWorldPos = hit.point;
                        finalNormal = hit.normal;
                        hitFound = true;
                    }
                }

                if (!hitFound)
                {
                    float y = CalculateHeight(worldPos, z, dist);
                    finalWorldPos = transform.TransformPoint(new Vector3(x, y, z));
                }

                finalWorldPos.y -= 0.15f;
                grassPositions.Add(finalWorldPos);
                grassNormals.Add(finalNormal);
            }
        }

        int batchCount = Mathf.CeilToInt(grassPositions.Count / 1023f);
        grassBatches = new Matrix4x4[batchCount][];
        Vector3 lossyScale = transform.lossyScale;

        for (int b = 0; b < batchCount; b++)
        {
            int start = b * 1023;
            int count = Mathf.Min(1023, grassPositions.Count - start);
            grassBatches[b] = new Matrix4x4[count];

            for (int i = 0; i < count; i++)
            {
                Quaternion groundTilt = Quaternion.FromToRotation(Vector3.up, grassNormals[start + i]);
                Quaternion yaw = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                Quaternion randomTilt = Quaternion.Euler(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f));

                float randomScale = Random.Range(grassMinScale, grassMaxScale);
                Vector3 finalScale = new Vector3(randomScale * lossyScale.x, randomScale * lossyScale.y, randomScale * lossyScale.z);

                grassBatches[b][i] = Matrix4x4.TRS(grassPositions[start + i], groundTilt * yaw * randomTilt, finalScale);
            }
        }
    }

    void GenerateEnvironment()
    {
        if (environmentPrefabs == null || environmentPrefabs.Count == 0) return;

        int cellsX = Mathf.FloorToInt(width / environmentGridStep);
        int cellsZ = Mathf.FloorToInt(length / environmentGridStep);

        MeshCollider chunkCollider = GetComponent<MeshCollider>();
        Transform envContainer = new GameObject("EnvironmentContainer").transform;
        envContainer.SetParent(transform);
        envContainer.localPosition = Vector3.zero;

        for (int cx = 0; cx < cellsX; cx++)
        {
            for (int cz = 0; cz < cellsZ; cz++)
            {
                EnvironmentObject envObj = environmentPrefabs[Random.Range(0, environmentPrefabs.Count)];

                if (envObj.prefab == null || Random.value > envObj.spawnProbability) continue;

                float x = Mathf.Lerp(terrainStartX, terrainEndX, (cx + Random.value) / cellsX);
                float z = Mathf.Lerp(terrainStartZ, terrainEndZ, (cz + Random.value) / cellsZ);

                Vector3 localPos = new Vector3(x, 0, z);
                Vector3 worldPos = transform.TransformPoint(localPos);
                float dist = distanceField.SampleDistance(worldPos);

                if (dist < flatZoneRadius + blendZone) continue;

                Vector3 finalWorldPos = worldPos;
                Vector3 finalNormal = Vector3.up;
                bool hitFound = false;

                if (chunkCollider != null && chunkCollider.sharedMesh != null)
                {
                    Vector3 rayStart = worldPos + Vector3.up * (heightScale * 2f + 100f);
                    if (chunkCollider.Raycast(new Ray(rayStart, Vector3.down), out RaycastHit hit, heightScale * 5f + 200f))
                    {
                        finalWorldPos = hit.point;
                        finalNormal = hit.normal;
                        hitFound = true;
                    }
                }

                if (!hitFound)
                {
                    float y = CalculateHeight(worldPos, z, dist);
                    finalWorldPos = transform.TransformPoint(new Vector3(x, y, z));
                }

                finalWorldPos.y += envObj.yOffset;

                GameObject instance = Instantiate(envObj.prefab, finalWorldPos, Quaternion.identity, envContainer);

                Quaternion yaw = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                if (envObj.alignToTerrain)
                {
                    Quaternion groundTilt = Quaternion.FromToRotation(Vector3.up, finalNormal);
                    instance.transform.rotation = groundTilt * yaw;
                }
                else
                {
                    instance.transform.rotation = yaw;
                }

                float scale = Random.Range(envObj.minScale, envObj.maxScale);
                instance.transform.localScale = new Vector3(scale, scale, scale);
            }
        }
    }

    // ---- Вспомогательный класс: поле расстояний ----
    class DistanceField
    {
        private float[] distances;
        private int gridSize;
        private float startX, endX, startZ, endZ;
        private Transform chunkTransform;

        public DistanceField(List<Renderer> obstacles, float sX, float eX, float sZ, float eZ, int gridRes, Transform trans)
        {
            startX = sX; endX = eX; startZ = sZ; endZ = eZ;
            gridSize = gridRes;
            chunkTransform = trans;
            distances = new float[gridSize * gridSize];

            if (obstacles.Count == 0)
            {
                for (int i = 0; i < distances.Length; i++) distances[i] = float.MaxValue;
                return;
            }

            float stepX = (endX - startX) / (gridSize - 1);
            float stepZ = (endZ - startZ) / (gridSize - 1);

            for (int z = 0; z < gridSize; z++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    Vector3 localPos = new Vector3(startX + x * stepX, 0, startZ + z * stepZ);
                    Vector3 worldPos = trans.TransformPoint(localPos);
                    float minDist = float.MaxValue;
                    foreach (var r in obstacles)
                    {
                        if (r == null) continue;
                        float d = Mathf.Sqrt(r.bounds.SqrDistance(worldPos));
                        if (d < minDist) minDist = d;
                    }
                    distances[z * gridSize + x] = minDist;
                }
            }
        }

        public float SampleDistance(Vector3 worldPoint)
        {
            Vector3 local = chunkTransform.InverseTransformPoint(worldPoint);
            float u = (local.x - startX) / (endX - startX);
            float v = (local.z - startZ) / (endZ - startZ);
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            float x = u * (gridSize - 1);
            float z = v * (gridSize - 1);
            int x0 = Mathf.FloorToInt(x);
            int z0 = Mathf.FloorToInt(z);
            int x1 = Mathf.Min(x0 + 1, gridSize - 1);
            int z1 = Mathf.Min(z0 + 1, gridSize - 1);
            float fx = x - x0;
            float fz = z - z0;

            float d00 = distances[z0 * gridSize + x0];
            float d10 = distances[z0 * gridSize + x1];
            float d01 = distances[z1 * gridSize + x0];
            float d11 = distances[z1 * gridSize + x1];

            float d0 = Mathf.Lerp(d00, d10, fx);
            float d1 = Mathf.Lerp(d01, d11, fx);
            return Mathf.Lerp(d0, d1, fz);
        }
    }
}