using UnityEngine;
using System.Collections.Generic;

public class RoadGenerator : MonoBehaviour
{
    [Header("Префабы дороги (Массивы)")]
    public GameObject[] straightPrefabs;
    public GameObject[] leftPrefabs;
    public GameObject[] rightPrefabs;

    [Header("Префабы окружения")]
    public GameObject[] environmentPrefabs;
    [Range(0, 10)] public int objectsPerChunk = 5;
    public float roadWidth = 6f;
    public float envZoneWidth = 15f;
    public float chunkLength = 20f;

    [Header("Настройки")]
    public Transform player;
    public int maxChunksAhead = 15;
    public float destroyDistance = 60f;

    [Header("Защита от пересечений")]
    public float minDistanceBetweenRoads = 20f;

    [Header("Вероятности")]
    [Range(0, 100)] public int straightChance = 75;

    private List<GameObject> spawnedChunks = new List<GameObject>();
    private Transform lastEndPoint;

    // Теперь храним позиции именно EndPoint'ов, это точнее для проверки пересечений
    private List<Vector3> roadEndPositions = new List<Vector3>();

    private int consecutiveTurns = 0;
    public int maxConsecutiveTurns = 2;

    void Start()
    {
        if (straightPrefabs.Length == 0 || leftPrefabs.Length == 0 || rightPrefabs.Length == 0 || !player)
        {
            Debug.LogError("❌ Не все массивы префабов дорог заполнены!");
            return;
        }

        SpawnChunk(straightPrefabs[0], true);
        while (spawnedChunks.Count < maxChunksAhead)
        {
            SpawnNextChunk();
        }
    }

    void Update()
    {
        if (player == null || spawnedChunks.Count == 0) return;

        GameObject oldestChunk = spawnedChunks[0];
        float distanceToOldest = Vector3.Distance(player.position, oldestChunk.transform.position);

        if (distanceToOldest > destroyDistance)
        {
            roadEndPositions.RemoveAt(0);
            Destroy(oldestChunk);
            spawnedChunks.RemoveAt(0);
            SpawnNextChunk();
        }
    }

    void SpawnNextChunk()
    {
        int randomVal = Random.Range(0, 100);
        GameObject[] chosenCategory;

        if (randomVal < straightChance) chosenCategory = straightPrefabs;
        else if (randomVal < straightChance + ((100 - straightChance) / 2)) chosenCategory = leftPrefabs;
        else chosenCategory = rightPrefabs;

        if (chosenCategory != straightPrefabs && consecutiveTurns >= maxConsecutiveTurns)
        {
            chosenCategory = straightPrefabs;
        }

        GameObject chosenPrefab = chosenCategory[Random.Range(0, chosenCategory.Length)];

        if (!IsPositionSafe(chosenPrefab))
        {
            if (chosenCategory != straightPrefabs)
            {
                chosenCategory = straightPrefabs;
                chosenPrefab = chosenCategory[Random.Range(0, chosenCategory.Length)];
            }
        }

        SpawnChunk(chosenPrefab, false);

        if (chosenCategory != straightPrefabs) consecutiveTurns++;
        else consecutiveTurns = 0;
    }

    bool IsPositionSafe(GameObject prefab)
    {
        if (lastEndPoint == null) return true;
        Transform start = prefab.transform.Find("StartPoint");
        Transform end = prefab.transform.Find("EndPoint");

        if (start == null || end == null) return true;

        // Предсказываем, где окажется конец новой дороги с учетом всех поворотов
        Vector3 localEndPos = start.InverseTransformPoint(end.position);
        Vector3 predictedEndPos = lastEndPoint.TransformPoint(localEndPos);

        // Проверяем, не воткнется ли этот кусок в уже существующую дорогу
        foreach (Vector3 pos in roadEndPositions)
        {
            if (Vector3.Distance(predictedEndPos, pos) < minDistanceBetweenRoads)
                return false;
        }
        return true;
    }

    void SpawnChunk(GameObject prefab, bool isFirstChunk)
    {
        GameObject newChunk = Instantiate(prefab);
        Transform startPoint = newChunk.transform.Find("StartPoint");
        Transform endPoint = newChunk.transform.Find("EndPoint");

        if (startPoint == null || endPoint == null)
        {
            Debug.LogError($"❌ Ошибка: В префабе {prefab.name} нет StartPoint или EndPoint!");
            return;
        }

        if (isFirstChunk || lastEndPoint == null)
        {
            newChunk.transform.position = Vector3.zero;
            newChunk.transform.rotation = Quaternion.identity;
        }
        else
        {
            // --- ИДЕАЛЬНАЯ СТЫКОВКА ---
            // 1. Выравниваем вращение (учитывая локальный поворот StartPoint внутри префаба)
            Quaternion rotationOffset = lastEndPoint.rotation * Quaternion.Inverse(startPoint.rotation);
            newChunk.transform.rotation = rotationOffset * newChunk.transform.rotation;

            // 2. Сдвигаем сам кусок так, чтобы StartPoint идеально встал в lastEndPoint
            Vector3 positionOffset = lastEndPoint.position - startPoint.position;
            newChunk.transform.position += positionOffset;
        }

        PopulateEnvironment(newChunk);

        spawnedChunks.Add(newChunk);
        roadEndPositions.Add(endPoint.position);
        lastEndPoint = endPoint;
    }

    void PopulateEnvironment(GameObject chunk)
    {
        if (environmentPrefabs == null || environmentPrefabs.Length == 0) return;

        for (int i = 0; i < objectsPerChunk; i++)
        {
            float side = Random.value > 0.5f ? 1f : -1f;
            float randomX = Random.Range(roadWidth, envZoneWidth) * side;
            float randomZ = Random.Range(-chunkLength / 2f, chunkLength / 2f);

            Vector3 localPos = new Vector3(randomX, 0f, randomZ);
            GameObject envPrefab = environmentPrefabs[Random.Range(0, environmentPrefabs.Length)];

            GameObject envObj = Instantiate(envPrefab, chunk.transform);
            envObj.transform.localPosition = localPos;
            envObj.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
        }
    }

    void OnDrawGizmos()
    {
        if (roadEndPositions == null) return;
        Gizmos.color = Color.red;
        foreach (Vector3 pos in roadEndPositions) Gizmos.DrawWireSphere(pos, minDistanceBetweenRoads / 2);
    }
}