using UnityEngine;
using System.Collections.Generic;

public class RoadGenerator : MonoBehaviour
{
    [Header("Префабы дороги")]
    public GameObject straightPrefab;
    public GameObject leftPrefab;
    public GameObject rightPrefab;

    [Header("Префабы окружения")]
    public GameObject[] environmentPrefabs; // Сюда перетащи деревья, камни, кусты
    [Range(0, 10)] public int objectsPerChunk = 5; // Сколько попыток спавна на один сегмент
    public float roadWidth = 6f; // Ширина дороги (в этой зоне ничего не спавним)
    public float envZoneWidth = 15f; // Как далеко от дороги могут расти объекты
    public float chunkLength = 20f; // Примерная длина одного сегмента

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
    private List<Vector3> roadPositions = new List<Vector3>();
    private int consecutiveTurns = 0;
    private int maxConsecutiveTurns = 3;

    void Start()
    {
        if (!straightPrefab || !leftPrefab || !rightPrefab || !player)
        {
            Debug.LogError("❌ Не все ссылки назначены!");
            return;
        }

        SpawnChunk(straightPrefab, true);
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
            roadPositions.RemoveAt(0);
            Destroy(oldestChunk);
            spawnedChunks.RemoveAt(0);
            SpawnNextChunk();
        }
    }

    void SpawnNextChunk()
    {
        int randomVal = Random.Range(0, 100);
        GameObject chosenPrefab;

        if (randomVal < straightChance) chosenPrefab = straightPrefab;
        else if (randomVal < straightChance + ((100 - straightChance) / 2)) chosenPrefab = leftPrefab;
        else chosenPrefab = rightPrefab;

        if (chosenPrefab != straightPrefab && consecutiveTurns >= maxConsecutiveTurns)
            chosenPrefab = straightPrefab;

        if (!IsPositionSafe(chosenPrefab))
        {
            if (chosenPrefab != straightPrefab) chosenPrefab = straightPrefab;
        }

        SpawnChunk(chosenPrefab, false);

        if (chosenPrefab != straightPrefab) consecutiveTurns++;
        else consecutiveTurns = 0;
    }

    bool IsPositionSafe(GameObject prefab)
    {
        if (lastEndPoint == null) return true;
        Transform startPoint = prefab.transform.Find("StartPoint");
        if (startPoint == null) return true;

        Vector3 localOffset = startPoint.position - prefab.transform.position;
        Vector3 rotatedOffset = lastEndPoint.rotation * localOffset;
        Vector3 predictedPosition = lastEndPoint.position - rotatedOffset;

        foreach (Vector3 pos in roadPositions)
        {
            if (Vector3.Distance(predictedPosition, pos) < minDistanceBetweenRoads) return false;
        }
        return true;
    }

    void SpawnChunk(GameObject prefab, bool isFirstChunk)
    {
        GameObject newChunk = Instantiate(prefab);
        Transform startPoint = newChunk.transform.Find("StartPoint");
        Transform endPoint = newChunk.transform.Find("EndPoint");

        if (isFirstChunk || lastEndPoint == null)
        {
            newChunk.transform.position = Vector3.zero;
            newChunk.transform.rotation = Quaternion.identity;
        }
        else
        {
            newChunk.transform.position = lastEndPoint.position;
            newChunk.transform.rotation = lastEndPoint.rotation;

            if (startPoint != null)
            {
                Vector3 offset = startPoint.position - newChunk.transform.position;
                newChunk.transform.position -= offset;
            }
        }

        // --- ГЕНЕРАЦИЯ ОКРУЖЕНИЯ ---
        PopulateEnvironment(newChunk);
        // --------------------------

        spawnedChunks.Add(newChunk);
        roadPositions.Add(newChunk.transform.position);
        lastEndPoint = endPoint;
    }

    void PopulateEnvironment(GameObject chunk)
    {
        if (environmentPrefabs == null || environmentPrefabs.Length == 0) return;

        for (int i = 0; i < objectsPerChunk; i++)
        {
            // 1. Определяем сторону (лево или право)
            float side = Random.value > 0.5f ? 1f : -1f;

            // 2. Выбираем случайную точку в локальных координатах чанка
            // X: от границы дороги до края зоны окружения
            float randomX = Random.Range(roadWidth, envZoneWidth) * side;
            // Z: случайная точка по длине сегмента
            float randomZ = Random.Range(-chunkLength / 2f, chunkLength / 2f);
            // Y: оставляем 0, чтобы объект стоял на поверхности (или подстрой под свои модели)
            float randomY = 0f;

            Vector3 localPos = new Vector3(randomX, randomY, randomZ);

            // 3. Выбираем случайный объект из библиотеки
            GameObject envPrefab = environmentPrefabs[Random.Range(0, environmentPrefabs.Length)];

            // 4. Спавним как дочерний объект чанка
            GameObject envObj = Instantiate(envPrefab, chunk.transform);
            envObj.transform.localPosition = localPos;

            // Случайный поворот по оси Y для естественности
            envObj.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
        }
    }

    void OnDrawGizmos()
    {
        if (roadPositions == null) return;
        Gizmos.color = Color.red;
        foreach (Vector3 pos in roadPositions) Gizmos.DrawWireSphere(pos, minDistanceBetweenRoads / 2);
    }
}
