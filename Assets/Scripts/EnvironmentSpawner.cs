using UnityEngine;

public class EnvironmentSpawner : MonoBehaviour
{
    public GameObject[] environmentPrefabs; // Деревья, здания, фонари
    public Transform player;

    public float spawnDistance = 100f;
    public float sideOffset = 15f; // Расстояние от дороги
    public float spawnInterval = 10f; // Интервал между объектами

    private float lastSpawnZ = 0f;

    void Update()
    {
        if (player.position.z > lastSpawnZ - spawnDistance)
        {
            SpawnEnvironment();
        }
    }

    void SpawnEnvironment()
    {
        // Спавним объекты по обе стороны дороги
        for (int i = 0; i < 2; i++)
        {
            int randomIndex = Random.Range(0, environmentPrefabs.Length);
            GameObject prefab = environmentPrefabs[randomIndex];

            // Позиция: слева или справа от дороги
            float side = (i == 0) ? -sideOffset : sideOffset;
            float randomZ = lastSpawnZ + Random.Range(-5f, 5f);

            Vector3 spawnPos = new Vector3(side, 0, randomZ);

            // Рандомный поворот
            Quaternion rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

            Instantiate(prefab, spawnPos, rotation);
        }

        lastSpawnZ += spawnInterval;
    }
}