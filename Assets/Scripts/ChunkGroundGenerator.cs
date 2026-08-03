using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ChunkGroundGenerator : MonoBehaviour
{
    [Header("Настройки ландшафта")]
    public float width = 40f;      // Общая ширина чанка (дорога + обочины)
    public float length = 50f;     // Длина чанка
    public int resolution = 10;    // Детализация сетки
    public float heightScale = 3f; // Высота холмов по бокам
    public float roadWidth = 8f;   // Ширина зоны дороги (там будет плоско)

    void Start()
    {
        GenerateTerrainMesh();
    }

    void GenerateTerrainMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        Mesh mesh = new Mesh();
        mesh.name = "ProceduralGround";

        int vertCountX = resolution + 1;
        int vertCountZ = resolution + 1;
        Vector3[] vertices = new Vector3[vertCountX * vertCountZ];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[resolution * resolution * 6];

        float stepX = width / resolution;
        float stepZ = length / resolution;

        // 1. Генерируем вершины
        int vertexIndex = 0;
        for (int z = 0; z < vertCountZ; z++)
        {
            for (int x = 0; x < vertCountX; x++)
            {
                float xPos = (x * stepX) - (width / 2f);
                float zPos = z * stepZ;

                float yPos = 0f;

                // Делаем дорогу по центру плоской, а по бокам поднимаем холмы с помощью Mathf.PerlinNoise
                float distanceFromCenter = Mathf.Abs(xPos);
                if (distanceFromCenter > (roadWidth / 2f))
                {
                    // Шум Перлина для создания реалистичных холмов и отаек
                    float noiseFactor = (distanceFromCenter - (roadWidth / 2f)) / (width / 2f);
                    yPos = Mathf.PerlinNoise(xPos * 0.05f + transform.position.x, zPos * 0.05f + transform.position.z) * heightScale * noiseFactor;
                }

                vertices[vertexIndex] = new Vector3(xPos, yPos, zPos);
                uv[vertexIndex] = new Vector2((float)x / resolution, (float)z / resolution);
                vertexIndex++;
            }
        }

        // 2. Генерируем треугольники
        int tris = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i0 = z * vertCountX + x;
                int i1 = (z + 1) * vertCountX + x;
                int i2 = (z + 1) * vertCountX + (x + 1);
                int i3 = z * vertCountX + (x + 1);

                // Первый треугольник квадрата
                triangles[tris + 0] = i0;
                triangles[tris + 1] = i1;
                triangles[tris + 2] = i2;

                // Второй треугольник квадрата
                triangles[tris + 3] = i0;
                triangles[tris + 4] = i2;
                triangles[tris + 5] = i3;

                tris += 6;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;

        // Автоматически добавляем коллайдер, чтобы машина не проваливалась сквозь землю
        MeshCollider collider = GetComponent<MeshCollider>();
        if (collider == null)
        {
            collider = gameObject.AddComponent<MeshCollider>();
        }
        collider.sharedMesh = mesh;
    }
}