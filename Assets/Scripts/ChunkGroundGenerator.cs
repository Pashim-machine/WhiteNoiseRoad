using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ChunkGroundGenerator : MonoBehaviour
{
    [Header("Настройки ландшафта")]
    public float width = 500f;
    public float length = 500f;
    public int resolution = 50;
    public float heightScale = 20f;
    public float noiseScale = 0.02f;

    [Header("Настройки обочины")]
    [Tooltip("Базовая ширина плоской обочины от края моделей")]
    public float roadCheckRadius = 6f;
    [Tooltip("Длина плавного подъема холма")]
    public float blendZone = 15f;

    [System.Serializable]
    public struct SceneryObject
    {
        public GameObject prefab;
        [Range(0.1f, 10f)] public float spawnWeight;
    }

    [Header("Декорации (Рандомный спавн)")]
    public List<SceneryObject> sceneryPrefabs;
    public int objectsCount = 40;

    private List<Renderer> obstacleRenderers = new List<Renderer>();
    private float actualFlatZone;

    void Start()
    {
        obstacleRenderers.Clear();

        // БОЛЬШЕ НИКАКИХ ТЕГОВ! Берем ВООБЩЕ ВСЕ визуальные модели внутри чанка
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>();

        foreach (Renderer rend in allRenderers)
        {
            // Добавляем в препятствия всё, кроме самой генерируемой земли,
            // чтобы земля не сплющила саму себя
            if (rend.gameObject != this.gameObject)
            {
                obstacleRenderers.Add(rend);
            }
        }

        if (obstacleRenderers.Count == 0)
        {
            Debug.LogWarning($"[Ландшафт] В чанке {gameObject.name} вообще нет объектов! Земле не под что подстраиваться.");
        }

        // Защита от наложения полигонов
        float stepX = width / resolution;
        float stepZ = length / resolution;
        float maxGridStep = Mathf.Max(stepX, stepZ);
        actualFlatZone = Mathf.Max(roadCheckRadius, maxGridStep * 1.5f);

        GenerateTerrainMesh();
        SpawnScenery();
    }

    float GetDistanceToObstacle(Vector3 worldPoint)
    {
        if (obstacleRenderers.Count == 0) return 0f;

        float minDistance = float.MaxValue;
        foreach (var rend in obstacleRenderers)
        {
            // Измеряем дистанцию до границ (Bounds) любой модели в чанке
            float dist = Mathf.Sqrt(rend.bounds.SqrDistance(worldPoint));
            if (dist < minDistance)
            {
                minDistance = dist;
            }
        }
        return minDistance;
    }

    void GenerateTerrainMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        Mesh mesh = new Mesh { name = "ProceduralGround" };

        int vertCountX = resolution + 1;
        int vertCountZ = resolution + 1;
        Vector3[] vertices = new Vector3[vertCountX * vertCountZ];
        Vector2[] uv = new Vector2[vertices.Length];

        // Список треугольников для основного меша (земля + дорога для коллайдера)
        int[] triangles = new int[resolution * resolution * 6];

        // Список треугольников ТОЛЬКО для травы (дорога исключена)
        List<int> grassTriangles = new List<int>();

        float stepX = width / resolution;
        float stepZ = length / resolution;

        int vertexIndex = 0;
        for (int z = 0; z < vertCountZ; z++)
        {
            for (int x = 0; x < vertCountX; x++)
            {
                float xPos = (x * stepX) - (width / 2f);
                float zPos = z * stepZ;

                Vector3 worldPos = transform.TransformPoint(new Vector3(xPos, 0, zPos));

                float distToObstacle = GetDistanceToObstacle(worldPos);
                float yPos = 0f;

                if (distToObstacle > actualFlatZone)
                {
                    float fadeFactor = Mathf.InverseLerp(actualFlatZone, actualFlatZone + blendZone, distToObstacle);
                    fadeFactor = Mathf.SmoothStep(0f, 1f, fadeFactor);

                    yPos = Mathf.PerlinNoise(worldPos.x * noiseScale, worldPos.z * noiseScale) * heightScale * fadeFactor;
                }

                vertices[vertexIndex] = new Vector3(xPos, yPos, zPos);
                uv[vertexIndex] = new Vector2((float)x / resolution, (float)z / resolution);
                vertexIndex++;
            }
        }

        int tris = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i0 = z * vertCountX + x;
                int i1 = (z + 1) * vertCountX + x;
                int i2 = (z + 1) * vertCountX + (x + 1);
                int i3 = z * vertCountX + (x + 1);

                // Добавляем треугольники в основной меш
                triangles[tris + 0] = i0;
                triangles[tris + 1] = i1;
                triangles[tris + 2] = i2;
                triangles[tris + 3] = i0;
                triangles[tris + 4] = i2;
                triangles[tris + 5] = i3;
                tris += 6;

                // Проверяем центр квадрата: если это не дорога/обочина, добавляем треугольники для травы
                Vector3 centerLocal = (vertices[i0] + vertices[i1] + vertices[i2] + vertices[i3]) / 4f;
                Vector3 centerWorld = transform.TransformPoint(centerLocal);
                float distToCenter = GetDistanceToObstacle(centerWorld);

                // Отступаем чуть дальше зоны обочины, чтобы трава не лезла на асфальт
                if (distToCenter > actualFlatZone + 1.5f)
                {
                    grassTriangles.Add(i0);
                    grassTriangles.Add(i1);
                    grassTriangles.Add(i2);
                    grassTriangles.Add(i0);
                    grassTriangles.Add(i2);
                    grassTriangles.Add(i3);
                }
            }
        }

        // 1. Собираем основной меш для земли и коллайдера
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        meshFilter.mesh = mesh;

        MeshCollider collider = GetComponent<MeshCollider>();
        if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;

        // 2. Создаем отдельный облегченный меш специально для травы (без дороги)
        Mesh grassMesh = new Mesh { name = "GrassOnlyMesh" };
        grassMesh.vertices = vertices;
        grassMesh.uv = uv;
        grassMesh.triangles = grassTriangles.ToArray();
        grassMesh.RecalculateNormals();

        // 3. Передаем чистый меш в VFX Graph
        VisualEffect vfx = GetComponent<VisualEffect>();
        if (vfx != null)
        {
            vfx.SetMesh("GroundMesh", grassMesh);
            vfx.Play();
        }
    }

    void SpawnScenery()
    {
        if (sceneryPrefabs == null || sceneryPrefabs.Count == 0) return;

        GameObject sceneryParent = new GameObject("SceneryContainer");
        sceneryParent.transform.SetParent(transform);
        sceneryParent.transform.localPosition = Vector3.zero;

        int spawned = 0;
        int attempts = 0;

        while (spawned < objectsCount && attempts < objectsCount * 4)
        {
            attempts++;

            float localX = Random.Range(-width / 2f, width / 2f);
            float localZ = Random.Range(2f, length - 2f);
            Vector3 worldPos = transform.TransformPoint(new Vector3(localX, 0, localZ));

            float dist = GetDistanceToObstacle(worldPos);

            if (dist < actualFlatZone + 2f) continue;

            float fadeFactor = Mathf.InverseLerp(actualFlatZone, actualFlatZone + blendZone, dist);
            fadeFactor = Mathf.SmoothStep(0f, 1f, fadeFactor);
            float yPos = Mathf.PerlinNoise(worldPos.x * noiseScale, worldPos.z * noiseScale) * heightScale * fadeFactor;

            GameObject selectedPrefab = GetRandomPrefab();
            if (selectedPrefab != null)
            {
                GameObject obj = Instantiate(selectedPrefab, sceneryParent.transform);
                obj.transform.localPosition = new Vector3(localX, yPos, localZ);
                obj.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                obj.transform.localScale = Vector3.one * Random.Range(0.8f, 1.4f);
                spawned++;
            }
        }
    }

    GameObject GetRandomPrefab()
    {
        float totalWeight = 0f;
        foreach (var item in sceneryPrefabs) totalWeight += item.spawnWeight;

        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;

        foreach (var item in sceneryPrefabs)
        {
            currentWeight += item.spawnWeight;
            if (randomValue <= currentWeight) return item.prefab;
        }
        return sceneryPrefabs[0].prefab;
    }


}