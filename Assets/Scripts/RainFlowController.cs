using UnityEngine;

public class RainFlowController : MonoBehaviour
{
    [Tooltip("Твоя физика машины")]
    public CarPhysics car;

    [Tooltip("Mesh Renderer твоего кузова (где висят все материалы)")]
    public Renderer bodyRenderer;

    [Tooltip("Индекс материала стекол (посмотри в инспекторе кузова, с нуля: Element 0 = 0, Element 1 = 1 и т.д.)")]
    public int glassMaterialIndex = 1;

    [Header("Поток капель")]
    public float gravityFlow = 0.4f;
    public float windFlow = 0.08f;
    public float maxFlow = 3f;

    private Vector3 localOffset;
    private Material glassMat;
    private static readonly int FlowOffsetOS = Shader.PropertyToID("_FlowOffsetOS");

    void Start()
    {
        // Достаем конкретный материал стекол из массива материалов кузова
        if (bodyRenderer != null && bodyRenderer.materials.Length > glassMaterialIndex)
        {
            glassMat = bodyRenderer.materials[glassMaterialIndex];
        }
        else
        {
            Debug.LogError("[RainFlow] Ошибка: Рендерер не назначен или индекс материала больше, чем есть на модели!");
        }
    }

    void LateUpdate()
    {
        if (glassMat == null) return;

        // Берем скорость прямо из твоего CarPhysics
        Vector3 vel = car != null ? car.GetVelocity() : Vector3.zero;

        // Считаем мировой вектор направления капель
        // Пробуем инвертировать гравитацию (например, если у машины перевернута ось Y)
        Vector3 worldFlow = Vector3.up * gravityFlow - vel * windFlow;
        worldFlow = Vector3.ClampMagnitude(worldFlow, maxFlow);

        // Переводим вектор в локальную систему координат кузова
        Vector3 localFlow = transform.InverseTransformDirection(worldFlow);

        // Копим смещение
        localOffset += localFlow * Time.deltaTime;

        // Сброс, чтобы цифры не улетели в космос
        if (localOffset.sqrMagnitude > 10000f) localOffset = Vector3.zero;

        // Передаем данные в шейдер
        glassMat.SetVector(FlowOffsetOS, localOffset);
    }
}