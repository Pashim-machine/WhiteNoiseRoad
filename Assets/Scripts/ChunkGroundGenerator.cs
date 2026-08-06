using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ChunkGroundGenerator : MonoBehaviour
{
    [Header("Ландшафт")]
    public float width = 500f;
    public float length = 500f;
    public int resolution = 50;
    public float heightScale = 20f;
    public float noiseScale = 0.02f;

    [Header("Препятствия (дороги, объекты)")]
    [Tooltip("Расстояние от границ объектов, на котором рельеф остаётся плоским")]
    public float flatZoneRadius = 8f;
    [Tooltip("Зона перехода к холмам")]
    public float blendZone = 15f;

    [Header("Трава (GPU Instancing)")]
    public Mesh grassQuadMesh;
    public Material grassMaterial;
    public float grassStep = 3f;
    public float grassMinScale = 0.6f;
    public float grassMaxScale = 1.4f;
    public int maxGrassPoints = 30000;

    private DistanceField distanceField;
    private List<Vector3> grassPositions = new List<Vector3>();
    private List<Vector3> grassNormals = new List<Vector3>();
    private Matrix4x4[][] grassBatches;
    private bool grassReady = false;

    void Start()
    {
        List<Renderer> obstacles = new List<Renderer>();

        // Защита: игнорируем препятствия, которые больше половины размера чанка (чтобы не заблокировать весь чанк)
        float maxObstacleSize = Mathf.Max(width, length) * 0.5f;
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r.gameObject != gameObject && r.bounds.size.magnitude < maxObstacleSize)
                obstacles.Add(r);
        }

        if (grassQuadMesh == null)
        {
            grassQuadMesh = CreateGrassQuadMesh();
        }

        distanceField = new DistanceField(obstacles, width, length, 64, transform);

        GenerateTerrainMesh();
        GenerateGrassPoints();
        grassReady = true;

        // Проверка и включение Instancing
        if (grassMaterial != null)
        {
            grassMaterial.enableInstancing = true;
            if (!grassMaterial.enableInstancing)
            {
                Debug.LogError($"Материал {grassMaterial.name} НЕ поддерживает GPU Instancing! Трава не будет отображаться. Включите галочку 'Enable GPU Instancing' в материале.");
            }
        }
        else
        {
            Debug.LogError("Материал травы не назначен!");
        }
    }

    void Update()
    {
        if (grassReady && grassBatches != null && grassMaterial != null && grassMaterial.enableInstancing)
        {
            foreach (var batch in grassBatches)
            {
                if (batch.Length > 0)
                {
                    // Используем полную сигнатуру с гигантскими Bounds, чтобы Unity не отсекала траву
                    Graphics.DrawMeshInstanced(grassQuadMesh, 0, grassMaterial, batch, batch.Length, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
                }
            }
        }
    }

    Mesh CreateGrassQuadMesh()
    {
        Mesh mesh = new Mesh { name = "GrassQuad" };

        Vector3[] vertices = new Vector3[8];
        Vector2[] uv = new Vector2[8];
        int[] triangles = new int[12];

        float hw = 0.5f;
        float h = 1.0f;

        vertices[0] = new Vector3(-hw, 0, 0);
        vertices[1] = new Vector3(hw, 0, 0);
        vertices[2] = new Vector3(-hw, h, 0);
        vertices[3] = new Vector3(hw, h, 0);

        vertices[4] = new Vector3(0, 0, -hw);
        vertices[5] = new Vector3(0, 0, hw);
        vertices[6] = new Vector3(0, h, -hw);
        vertices[7] = new Vector3(0, h, hw);

        uv[0] = new Vector2(0, 0); uv[1] = new Vector2(1, 0); uv[2] = new Vector2(0, 1); uv[3] = new Vector2(1, 1);
        uv[4] = new Vector2(0, 0); uv[5] = new Vector2(1, 0); uv[6] = new Vector2(0, 1); uv[7] = new Vector2(1, 1);

        triangles[0] = 0; triangles[1] = 1; triangles[2] = 2;
        triangles[3] = 2; triangles[4] = 1; triangles[5] = 3;

        triangles[6] = 4; triangles[7] = 5; triangles[8] = 6;
        triangles[9] = 6; triangles[10] = 5; triangles[11] = 7;

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        // КРИТИЧЕСКИ ВАЖНО: Задаем гигантские Bounds, чтобы внутренний куллинг Unity не удалял пачки травы
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(10000f, 10000f, 10000f));
        return mesh;
    }

    void GenerateTerrainMesh()
    {
        Mesh mesh = new Mesh { name = "ProceduralGround" };
        int vertCountX = resolution + 1;
        int vertCountZ = resolution + 1;
        int totalVerts = vertCountX * vertCountZ;
        Vector3[] vertices = new Vector3[totalVerts];
        Vector2[] uv = new Vector2[totalVerts];
        int[] triangles = new int[resolution * resolution * 6];

        float stepX = width / resolution;
        float stepZ = length / resolution;

        for (int z = 0, i = 0; z < vertCountZ; z++)
        {
            for (int x = 0; x < vertCountX; x++, i++)
            {
                float localX = (x * stepX) - (width * 0.5f);
                float localZ = z * stepZ;
                Vector3 worldPos = transform.TransformPoint(new Vector3(localX, 0, localZ));
                float dist = distanceField.SampleDistance(worldPos);

                float y = 0f;
                if (dist > flatZoneRadius)
                {
                    float fade = Mathf.InverseLerp(flatZoneRadius, flatZoneRadius + blendZone, dist);
                    fade = Mathf.SmoothStep(0f, 1f, fade);
                    y = Mathf.PerlinNoise(localX * noiseScale, localZ * noiseScale) * heightScale * fade;
                }

                vertices[i] = new Vector3(localX, y, localZ);
                uv[i] = new Vector2((float)x / resolution, (float)z / resolution);
            }
        }

        int triIndex = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i0 = z * vertCountX + x;
                int i1 = i0 + vertCountX;
                int i2 = i1 + 1;
                int i3 = i0 + 1;

                triangles[triIndex++] = i0;
                triangles[triIndex++] = i1;
                triangles[triIndex++] = i2;
                triangles[triIndex++] = i0;
                triangles[triIndex++] = i2;
                triangles[triIndex++] = i3;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        GetComponent<MeshFilter>().mesh = mesh;

        var col = GetComponent<MeshCollider>();
        if (!col) col = gameObject.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;
    }

    void GenerateGrassPoints()
    {
        grassPositions.Clear();
        grassNormals.Clear();
        float halfW = width * 0.5f;

        float cellSize = grassStep;
        int cellsX = Mathf.FloorToInt(width / cellSize);
        int cellsZ = Mathf.FloorToInt(length / cellSize);

        MeshCollider chunkCollider = GetComponent<MeshCollider>();

        for (int cx = 0; cx < cellsX; cx++)
        {
            for (int cz = 0; cz < cellsZ; cz++)
            {
                if (grassPositions.Count >= maxGrassPoints)
                    break;

                float jitterX = Random.value;
                float jitterZ = Random.value;

                float x = (cx + jitterX) * cellSize - halfW;
                float z = (cz + jitterZ) * cellSize;

                Vector3 localPos = new Vector3(x, 0, z);
                Vector3 worldPos = transform.TransformPoint(localPos);
                float dist = distanceField.SampleDistance(worldPos);

                if (dist < flatZoneRadius + 1.0f)
                    continue;

                Vector3 finalWorldPos = worldPos;
                Vector3 finalNormal = Vector3.up;
                bool hitFound = false;

                if (chunkCollider != null && chunkCollider.sharedMesh != null)
                {
                    Vector3 rayStart = worldPos + Vector3.up * (heightScale * 2f + 100f);
                    Ray ray = new Ray(rayStart, Vector3.down);
                    if (chunkCollider.Raycast(ray, out RaycastHit hit, heightScale * 5f + 200f))
                    {
                        finalWorldPos = hit.point;
                        finalNormal = hit.normal;
                        if (finalNormal.sqrMagnitude < 0.1f) finalNormal = Vector3.up;
                        finalNormal.Normalize();
                        hitFound = true;
                    }
                }

                if (!hitFound)
                {
                    float fade = Mathf.InverseLerp(flatZoneRadius, flatZoneRadius + blendZone, dist);
                    fade = Mathf.SmoothStep(0f, 1f, fade);
                    float y = Mathf.PerlinNoise(x * noiseScale, z * noiseScale) * heightScale * fade;
                    finalWorldPos = transform.TransformPoint(new Vector3(x, y, z));
                }

                finalWorldPos.y -= 0.15f;

                grassPositions.Add(finalWorldPos);
                grassNormals.Add(finalNormal);
            }
            if (grassPositions.Count >= maxGrassPoints) break;
        }

        int batchCount = Mathf.CeilToInt(grassPositions.Count / 1023f);
        grassBatches = new Matrix4x4[batchCount][];

        Vector3 lossyScale = transform.lossyScale;
        if (lossyScale.x == 0) lossyScale.x = 1;
        if (lossyScale.y == 0) lossyScale.y = 1;
        if (lossyScale.z == 0) lossyScale.z = 1;

        for (int b = 0; b < batchCount; b++)
        {
            int start = b * 1023;
            int count = Mathf.Min(1023, grassPositions.Count - start);
            grassBatches[b] = new Matrix4x4[count];

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = grassPositions[start + i];
                Vector3 normal = grassNormals[start + i];

                Quaternion groundTilt = Quaternion.FromToRotation(Vector3.up, normal);
                Quaternion yaw = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                Quaternion randomTilt = Quaternion.Euler(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f));
                Quaternion rot = groundTilt * yaw * randomTilt;

                float randomScale = Random.Range(grassMinScale, grassMaxScale);
                Vector3 finalScale = new Vector3(
                    randomScale * lossyScale.x,
                    randomScale * lossyScale.y,
                    randomScale * lossyScale.z
                );

                grassBatches[b][i] = Matrix4x4.TRS(pos, rot, finalScale);
            }
        }

        // Выводим точное количество в консоль для проверки
        Debug.Log($"[Трава] Сгенерировано точек: {grassPositions.Count}, создано батчей: {batchCount}");
    }

    // ---- Вспомогательный класс: поле расстояний ----
    class DistanceField
    {
        private float[] distances;
        private int gridSize;
        private float chunkWidth, chunkLength;
        private Transform chunkTransform;

        public DistanceField(List<Renderer> obstacles, float width, float length, int gridRes, Transform trans)
        {
            chunkWidth = width;
            chunkLength = length;
            gridSize = gridRes;
            chunkTransform = trans;
            distances = new float[gridSize * gridSize];

            if (obstacles.Count == 0)
            {
                for (int i = 0; i < distances.Length; i++)
                    distances[i] = float.MaxValue;
                return;
            }

            float stepX = width / (gridSize - 1);
            float stepZ = length / (gridSize - 1);
            for (int z = 0; z < gridSize; z++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    Vector3 localPos = new Vector3(x * stepX - width * 0.5f, 0, z * stepZ);
                    Vector3 worldPos = trans.TransformPoint(localPos);
                    float minDist = float.MaxValue;
                    foreach (var r in obstacles)
                    {
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
            float u = (local.x + chunkWidth * 0.5f) / chunkWidth;
            float v = local.z / chunkLength;
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