using UnityEngine;
using System.Collections.Generic;

public class ChunkSpawner : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Перетащи сюда объект машины")]
    public Transform playerTransform;
    [Tooltip("Массив префабов чанков с настроенными StartPoint и EndPoint")]
    public GameObject[] chunkPrefabs;

    [Header("Настройки генерации")]
    public int visibleChunksCount = 6; // Сколько чанков одновременно существует на сцене

    private List<GameObject> activeChunks = new List<GameObject>();
    private Vector3 nextSpawnPosition = Vector3.zero;
    private Quaternion nextSpawnRotation = Quaternion.identity;

    void Start()
    {
        if (playerTransform == null && Camera.main != null)
        {
            playerTransform = Camera.main.transform;
        }

        // Стартовые координаты для первого чанка
        nextSpawnPosition = Vector3.zero;
        nextSpawnRotation = Quaternion.identity;

        // Генерируем начальный набор чанков
        for (int i = 0; i < visibleChunksCount; i++)
        {
            SpawnChunk();
        }
    }

    void Update()
    {
        if (playerTransform == null || activeChunks.Count == 0) return;

        // Проверяем дистанцию: если машина проехала достаточно далеко, спавним новый чанк вперед
        if (playerTransform.position.z > (nextSpawnPosition.z - (visibleChunksCount * 30f)))
        {
            SpawnChunk();
            RemoveOldChunk();
        }
    }

    void SpawnChunk()
    {
        if (chunkPrefabs == null || chunkPrefabs.Length == 0) return;

        // Выбираем случайный префаб
        int randomIndex = Random.Range(0, chunkPrefabs.Length);
        GameObject prefabToSpawn = chunkPrefabs[randomIndex];

        // Инстантиируем чанк
        GameObject newChunk = Instantiate(prefabToSpawn);
        RoadChunk chunkData = newChunk.GetComponent<RoadChunk>();

        if (chunkData != null && chunkData.startPoint != null && chunkData.endPoint != null)
        {
            // Магия сокетов: сдвигаем чанк так, чтобы его StartPoint точно совпал с точкой стыка (nextSpawnPosition)
            Vector3 offset = chunkData.startPoint.position - newChunk.transform.position;
            newChunk.transform.position = nextSpawnPosition - offset;
            newChunk.transform.rotation = nextSpawnRotation;

            // Запоминаем позицию и поворот EndPoint для следующего чанка
            nextSpawnPosition = chunkData.endPoint.position;
            nextSpawnRotation = chunkData.endPoint.rotation;
        }
        else
        {
            // Резервный вариант на случай, если забыли повесить скрипт RoadChunk
            newChunk.transform.position = nextSpawnPosition;
            newChunk.transform.rotation = nextSpawnRotation;
            nextSpawnPosition += new Vector3(0f, 0f, 50f);
        }

        activeChunks.Add(newChunk);
    }

    void RemoveOldChunk()
    {
        if (activeChunks.Count > visibleChunksCount)
        {
            GameObject oldestChunk = activeChunks[0];
            activeChunks.RemoveAt(0);
            Destroy(oldestChunk);
        }
    }
}