using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    [Tooltip("Скорость вращения солнца")]
    public float timeMultiplier = 2f;

    void Update()
    {
        transform.Rotate(Vector3.right * timeMultiplier * Time.deltaTime);
    }
}