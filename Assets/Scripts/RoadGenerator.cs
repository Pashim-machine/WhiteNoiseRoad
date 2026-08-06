using System.Collections.Generic;
using UnityEngine;

public class RoadGenerator : MonoBehaviour
{
    [Header("Префабы чанков (с RoadChunk и ChunkGroundGenerator)")]
    [Tooltip("Просто перетащи сюда все варианты твоих чанков")]
    public GameObject[] chunkPrefabs;

    [Header("Привязка к игроку")]
    [Tooltip("Трансформ игрока или машины, за которым следит генератор")]
    public Transform player;

    [Header("Настройки спавна")]
    [Tooltip("Сколько чанков должно быть впереди игрока")]
    public int maxChunksAhead = 10;
    [Tooltip("Дистанция позади игрока, после которой чанк удаляется")]
    public float destroyDistance = 80f;

    private List<GameObject> spawnedChunks = new List<GameObject>();
    private Transform lastEndPoint;

    void Start()
    {
        if (chunkPrefabs == null || chunkPrefabs.Length == 0 || !player) return;

        // Спавним случайный первый чанк
        int firstIndex = Random.Range(0, chunkPrefabs.Length);
        SpawnChunk(chunkPrefabs[firstIndex], true);

        while (spawnedChunks.Count < maxChunksAhead)
        {
            SpawnNextChunk();
        }
    }

    void Update()
    {
        if (player == null || spawnedChunks.Count == 0) return;

        // Проверяем самый старый чанк (который позади)
        GameObject oldestChunk = spawnedChunks[0];

        // Считаем дистанцию от игрока до старого чанка
        float distanceToOldest = Vector3.Distance(player.position, oldestChunk.transform.position);

        // Если игрок уехал достаточно далеко вперед — удаляем старый и генерим новый впереди
        if (distanceToOldest > destroyDistance && player.position.z > oldestChunk.transform.position.z)
        {
            Destroy(oldestChunk);
            spawnedChunks.RemoveAt(0);

            SpawnNextChunk();
        }
    }

    void SpawnNextChunk()
    {
        int index = Random.Range(0, chunkPrefabs.Length);
        Debug.Log($"Спавним чанк с индексом: {index} — {chunkPrefabs[index].name}");
        SpawnChunk(chunkPrefabs[index], false);
    }

    void SpawnChunk(GameObject prefab, bool isFirstChunk)
    {
        // 1. Создаем чанк
        GameObject newChunk = Instantiate(prefab);

        // 2. Ищем точки StartPoint и EndPoint (через компонент RoadChunk)
        RoadChunk roadChunk = newChunk.GetComponent<RoadChunk>();

        Transform startPoint = (roadChunk != null && roadChunk.startPoint != null)
            ? roadChunk.startPoint
            : newChunk.transform.Find("StartPoint");

        Transform endPoint = (roadChunk != null && roadChunk.endPoint != null)
            ? roadChunk.endPoint
            : newChunk.transform.Find("EndPoint");

        if (startPoint == null || endPoint == null)
        {
            Debug.LogError($"❌ На префабе '{prefab.name}' не найдены StartPoint или EndPoint! Проверь скрипт RoadChunk.");
            Destroy(newChunk);
            return;
        }

        // 3. Выравниваем и стыкуем чанк
        if (isFirstChunk || lastEndPoint == null)
        {
            newChunk.transform.position = Vector3.zero;
            newChunk.transform.rotation = Quaternion.identity;
        }
        else
        {
            // Поворачиваем чанк так, чтобы его StartPoint совпал по углу с прошлым EndPoint
            Quaternion rotationOffset = lastEndPoint.rotation * Quaternion.Inverse(startPoint.rotation);
            newChunk.transform.rotation = rotationOffset * newChunk.transform.rotation;

            // Сдвигаем чанк строго в позицию прошлого EndPoint
            Vector3 positionOffset = lastEndPoint.position - startPoint.position;
            newChunk.transform.position += positionOffset;
        }

        // 4. ТЕПЕРЬ запускаем генерацию земли, когда чанк встал на свое идеальное место!
        ChunkGroundGenerator groundGen = newChunk.GetComponent<ChunkGroundGenerator>();
        if (groundGen != null)
        {
            groundGen.InitChunk();
        }

        // 5. Запоминаем новый EndPoint и добавляем в список активных чанков
        lastEndPoint = endPoint;
        spawnedChunks.Add(newChunk);
    }
}