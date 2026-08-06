using UnityEngine;
using UnityEngine.InputSystem;

public class CameraLook : MonoBehaviour
{
    [Header("Настройки камеры")]
    public float sensitivity = 2f;
    public float maxYAngle = 80f;
    public float minYAngle = -80f;

    [Header("Плавность")]
    public float rotationSmoothTime = 0.12f; // Время сглаживания поворота

    [Header("Эффекты погружения")]
    public Camera targetCamera;
    public Rigidbody carRigidbody;
    public float baseFOV = 60f;
    public float maxSpeedForFOV = 30f;
    public float maxFOVPenalty = 15f;
    public float shakeIntensity = 0.05f;
    public float shakeFrequency = 15f; // Скорость тряски

    [Header("G-Force (Наклон в поворотах)")]
    public float maxRollAngle = 4f; // Максимальный наклон камеры в градусах
    public float rollSmoothSpeed = 5f;

    // Приватные переменные
    private float rotationX = 0f;
    private float rotationY = 0f;
    private Vector3 originalLocalPosition;
    private float currentRoll = 0f;
    private float yawVelocity; // Для SmoothDamp
    private float pitchVelocity; // Для SmoothDamp

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        originalLocalPosition = transform.localPosition;

        if (targetCamera == null) targetCamera = GetComponentInChildren<Camera>();
        if (carRigidbody == null) carRigidbody = GetComponentInParent<Rigidbody>();
    }

    void Update()
    {
        HandleMouseLook();
    }

    void LateUpdate()
    {
        HandleKeyboardInputs();
        ApplyCameraEffects();
    }

    private void HandleMouseLook()
    {
        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
        {
            float mouseX = Mouse.current.delta.x.ReadValue() * sensitivity * 0.1f;
            float mouseY = Mouse.current.delta.y.ReadValue() * sensitivity * 0.1f;

            rotationY += mouseX;
            rotationX -= mouseY;
            rotationX = Mathf.Clamp(rotationX, minYAngle, maxYAngle);
        }

        // Плавное вращение с помощью SmoothDampAngle (убирает рывки мыши)
        float smoothYaw = Mathf.SmoothDampAngle(transform.localEulerAngles.y, rotationY, ref yawVelocity, rotationSmoothTime);
        float smoothPitch = Mathf.SmoothDampAngle(transform.localEulerAngles.x, rotationX, ref pitchVelocity, rotationSmoothTime);

        // Наклон камеры (Roll) при повороте машины
        Vector3 localVel = carRigidbody.transform.InverseTransformDirection(carRigidbody.linearVelocity);
        // Если машина скользит вбок (дрифт) или поворачивает, наклоняем камеру
        float targetRoll = -localVel.x * maxRollAngle * 0.1f;
        targetRoll = Mathf.Clamp(targetRoll, -maxRollAngle, maxRollAngle);
        currentRoll = Mathf.Lerp(currentRoll, targetRoll, Time.deltaTime * rollSmoothSpeed);

        // Применяем финальное вращение (X - вверх/вниз, Y - влево/вправо, Z - наклон в повороте)
        transform.localRotation = Quaternion.Euler(smoothPitch, smoothYaw, currentRoll);
    }

    private void HandleKeyboardInputs()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[Key.R].wasPressedThisFrame)
        {
            rotationX = 0f;
            rotationY = 0f;
            yawVelocity = 0f;
            pitchVelocity = 0f;
        }

        if (Keyboard.current[Key.Escape].wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void ApplyCameraEffects()
    {
        if (targetCamera == null || carRigidbody == null) return;

        float currentSpeed = carRigidbody.linearVelocity.magnitude;
        float speedRatio = Mathf.Clamp01(currentSpeed / maxSpeedForFOV);

        // 1. Динамический FOV
        float targetFOV = baseFOV + (maxFOVPenalty * speedRatio);
        targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, targetFOV, Time.deltaTime * 5f);

        // 2. Плавная тряска на шумах Перлина (никаких резких рывков)
        if (currentSpeed > 1f)
        {
            float shakeMultiplier = speedRatio * shakeIntensity;

            // Генерируем плавный псевдо-случайный шум
            float shakeX = (Mathf.PerlinNoise(Time.time * shakeFrequency, 0f) - 0.5f) * 2f * shakeMultiplier;
            float shakeY = (Mathf.PerlinNoise(0f, Time.time * shakeFrequency) - 0.5f) * 2f * shakeMultiplier;

            Vector3 shakeOffset = new Vector3(shakeX, shakeY, 0f);
            transform.localPosition = Vector3.Lerp(transform.localPosition, originalLocalPosition + shakeOffset, Time.deltaTime * 10f);
        }
        else
        {
            // Плавное возвращение в центр при остановке
            transform.localPosition = Vector3.Lerp(transform.localPosition, originalLocalPosition, Time.deltaTime * 5f);
        }
    }
}