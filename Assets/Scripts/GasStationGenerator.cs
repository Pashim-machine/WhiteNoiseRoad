using UnityEngine;

[ExecuteAlways]
public class GasStationGenerator : MonoBehaviour
{
    [Header("Размеры здания (Магазин)")]
    public float width = 6f;
    public float length = 8f;
    public float height = 3.5f;

    [Header("Навес для колонок")]
    public bool hasCanopy = true;
    public float canopyWidth = 7f;
    public float canopyLength = 6f;
    public float canopyHeight = 4f;
    public float canopyForwardOffset = 5f;

    [Header("Материалы")]
    public Material wallMaterial;
    public Material roofMaterial;
    public Material metalMaterial;

    [ContextMenu("Сгенерировать заправку")]
    public void Generate()
    {
        // Очищаем старые дочерние объекты при перегенерации
        while (transform.childCount > 0)
        {
            DestroyImmediate(transform.GetChild(0).gameObject);
        }

        // 1. Создаем основное здание (магазин)
        GameObject building = GameObject.CreatePrimitive(PrimitiveType.Cube);
        building.name = "Building_Body";
        building.transform.SetParent(transform);
        building.transform.localPosition = new Vector3(0, height / 2f, 0);
        building.transform.localScale = new Vector3(width, height, length);
        ApplyMaterial(building, wallMaterial);

        // 2. Создаем крышу здания
        GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "Building_Roof";
        roof.transform.SetParent(transform);
        roof.transform.localPosition = new Vector3(0, height + 0.1f, 0);
        roof.transform.localScale = new Vector3(width + 0.6f, 0.2f, length + 0.6f);
        ApplyMaterial(roof, roofMaterial);

        // 3. Создаем вывеску / козырек магазина
        GameObject sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sign.name = "Sign_Board";
        sign.transform.SetParent(transform);
        sign.transform.localPosition = new Vector3(0, height - 0.5f, length / 2f + 0.1f);
        sign.transform.localScale = new Vector3(width * 0.8f, 0.8f, 0.2f);
        ApplyMaterial(sign, metalMaterial);

        if (hasCanopy)
        {
            float canopyZ = length / 2f + canopyForwardOffset;

            // Крыша навеса
            GameObject canopyRoof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            canopyRoof.name = "Canopy_Roof";
            canopyRoof.transform.SetParent(transform);
            canopyRoof.transform.localPosition = new Vector3(0, canopyHeight, canopyZ);
            canopyRoof.transform.localScale = new Vector3(canopyWidth, 0.3f, canopyLength);
            ApplyMaterial(canopyRoof, roofMaterial);

            // Опорные столбы навеса (4 штуки)
            float pX = canopyWidth / 2f - 0.3f;
            float pZ = canopyLength / 2f - 0.3f;

            CreatePillar(new Vector3(-pX, canopyHeight / 2f, canopyZ - pZ));
            CreatePillar(new Vector3(pX, canopyHeight / 2f, canopyZ - pZ));
            CreatePillar(new Vector3(-pX, canopyHeight / 2f, canopyZ + pZ));
            CreatePillar(new Vector3(pX, canopyHeight / 2f, canopyZ + pZ));

            // Топливные колонки под навесом
            CreatePump(new Vector3(-1.5f, 0.75f, canopyZ));
            CreatePump(new Vector3(1.5f, 0.75f, canopyZ));
        }

        Debug.Log("Заправка успешно сгенерирована!");
    }

    void CreatePillar(Vector3 localPos)
    {
        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "Canopy_Pillar";
        pillar.transform.SetParent(transform);
        pillar.transform.localPosition = localPos;
        pillar.transform.localScale = new Vector3(0.4f, canopyHeight / 2f, 0.4f);
        ApplyMaterial(pillar, metalMaterial);
    }

    void CreatePump(Vector3 localPos)
    {
        GameObject pump = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pump.name = "Gas_Pump";
        pump.transform.SetParent(transform);
        pump.transform.localPosition = localPos;
        pump.transform.localScale = new Vector3(0.8f, 1.5f, 0.6f);
        ApplyMaterial(pump, metalMaterial);
    }

    void ApplyMaterial(GameObject obj, Material mat)
    {
        if (mat != null)
        {
            Renderer rend = obj.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = mat;
        }
    }
}