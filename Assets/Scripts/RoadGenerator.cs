using System.Collections.Generic;
using UnityEngine;

public class RoadGenerator : MonoBehaviour
{
    [Header("Префабы чанков (с RoadChunk и ChunkGroundGenerator)")]
    public GameObject[] chunkPrefabs;

    [Header("Привязка к игроку")]
    public Transform player;

    [Header("Настройки спавна")]
    [Tooltip("Конец дороги всегда держится минимум в этой дистанции ВПЕРЕДИ игрока. Ставь ~1.5-2 длины чанка.")]
    public float aheadDistance = 300f;
    [Tooltip("Дистанция ПОЗАДИ игрока, после которой чанк удаляется. Большое значение = можно ехать назад.")]
    public float destroyDistance = 1000f;
    [Tooltip("Предохранитель памяти: максимум ОДНОВРЕМЕННЫХ чанков. Не блокирует спавн в нормальной игре.")]
    public int maxTotalChunks = 40;

    private readonly List<GameObject> spawnedChunks = new List<GameObject>();

    // Копия точки стыковки вместо ссылки на Transform убитого чанка
    private Vector3 anchorPos;
    private Quaternion anchorRot = Quaternion.identity;
    private bool hasAnchor;

    private float nextRetryTime;
    private bool capWarned;

    void Start()
    {
        if (chunkPrefabs == null || chunkPrefabs.Length == 0)
        {
            Debug.LogError("[RoadGenerator] chunkPrefabs пуст!");
            return;
        }
        if (player == null)
        {
            Debug.LogError("[RoadGenerator] Не назначен player!");
            return;
        }

        SpawnNextChunkSafe(true);
        ExtendRoad(); // сразу наращиваем дорогу вперёд по aheadDistance

        Debug.Log($"[RoadGenerator] Старт: собрано чанков {spawnedChunks.Count}");
    }

    void Update()
    {
        if (player == null) return;

        // 1. Отрезаем хвост только после destroyDistance (можно ехать назад)
        while (spawnedChunks.Count > 0)
        {
            GameObject oldest = spawnedChunks[0];
            if (oldest == null) { spawnedChunks.RemoveAt(0); continue; }
            if (Vector3.Distance(player.position, oldest.transform.position) > destroyDistance)
            {
                Debug.Log($"[RoadGenerator] Удалил чанк позади: {oldest.name}");
                Destroy(oldest);
                spawnedChunks.RemoveAt(0);
            }
            else break;
        }

        // 2. Достраиваем вперёд: единственное условие — конец дороги ближе aheadDistance
        ExtendRoad();
    }

    void ExtendRoad()
    {
        int guard = 0;
        while (guard++ < 16)
        {
            if (Time.time < nextRetryTime) break;
            if (spawnedChunks.Count >= maxTotalChunks)
            {
                if (!capWarned)
                {
                    capWarned = true;
                    Debug.LogWarning($"[RoadGenerator] Достигнут лимит maxTotalChunks={maxTotalChunks}. Увеличь его, если чанки короткие.");
                }
                break;
            }
            // Конец дороги достаточно далеко — спавнить не нужно
            if (hasAnchor && Vector3.Distance(player.position, anchorPos) > aheadDistance) break;

            if (SpawnNextChunkSafe(false)) continue;
            nextRetryTime = Time.time + 2f;
            break;
        }
    }

    bool SpawnNextChunkSafe(bool isFirstChunk)
    {
        List<GameObject> valid = new List<GameObject>(chunkPrefabs.Length);
        for (int i = 0; i < chunkPrefabs.Length; i++)
            if (chunkPrefabs[i] != null) valid.Add(chunkPrefabs[i]);

        if (valid.Count == 0)
        {
            Debug.LogError("[RoadGenerator] Все слоты chunkPrefabs пустые (Missing)!");
            return false;
        }
        return SpawnChunk(valid[Random.Range(0, valid.Count)], isFirstChunk);
    }

    bool SpawnChunk(GameObject prefab, bool isFirstChunk)
    {
        GameObject newChunk = null;
        try
        {
            newChunk = Instantiate(prefab);
            SanitizeEmptyMeshes(newChunk);

            RoadChunk roadChunk = newChunk.GetComponent<RoadChunk>();
            Transform startPoint = (roadChunk != null && roadChunk.startPoint != null)
                ? roadChunk.startPoint
                : newChunk.transform.Find("StartPoint");
            Transform endPoint = (roadChunk != null && roadChunk.endPoint != null)
                ? roadChunk.endPoint
                : newChunk.transform.Find("EndPoint");

            if (startPoint == null || endPoint == null)
            {
                Debug.LogError($"[RoadGenerator] На префабе '{prefab.name}' нет StartPoint/EndPoint!", prefab);
                Destroy(newChunk);
                return false;
            }

            if (isFirstChunk || !hasAnchor)
            {
                newChunk.transform.position = Vector3.zero;
                newChunk.transform.rotation = Quaternion.identity;
            }
            else
            {
                newChunk.transform.rotation = anchorRot * Quaternion.Inverse(startPoint.rotation) * newChunk.transform.rotation;
                newChunk.transform.position += anchorPos - startPoint.position;
            }

            ChunkGroundGenerator groundGen = newChunk.GetComponent<ChunkGroundGenerator>();
            if (groundGen != null) groundGen.InitChunk();

            anchorPos = endPoint.position;
            anchorRot = endPoint.rotation;
            hasAnchor = true;

            spawnedChunks.Add(newChunk);
            Debug.Log($"[RoadGenerator] Спавн: {newChunk.name} (всего {spawnedChunks.Count})");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RoadGenerator] Исключение при спавне чанка '{prefab.name}':\n{e}",
                newChunk != null ? newChunk : (Object)this);
            if (newChunk != null) Destroy(newChunk);
            return false;
        }
    }

    static void SanitizeEmptyMeshes(GameObject go)
    {
        MeshFilter[] filters = go.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            Mesh m = filters[i].sharedMesh;
            if (m != null && (m.subMeshCount == 0 || m.vertexCount == 0))
                filters[i].sharedMesh = null;
        }
    }
}