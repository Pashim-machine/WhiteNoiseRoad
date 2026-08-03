using UnityEngine;

public class RoadChunk : MonoBehaviour
{
    [Tooltip("Точка начала чанка (стыкуется с предыдущим)")]
    public Transform startPoint;

    [Tooltip("Точка конца чанка (сюда стыкуется следующий)")]
    public Transform endPoint;
}