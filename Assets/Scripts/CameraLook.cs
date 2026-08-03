using UnityEngine;
using UnityEngine.InputSystem;

public class CameraLook : MonoBehaviour
{
    [Header("Настройки камеры")]
    public float sensitivity = 2f;
    public float maxYAngle = 80f;
    public float minYAngle = -80f;

    [Header("Плавность")]
    public bool enableSmoothing = true;
    public float smoothSpeed = 15f;

    [Header("Эффекты погружения")]
    public Camera targetCamera;
    public Rigidbody carRigidbody;
    public float baseFOV = 60f;
    public float maxSpeedForFOV = 30f; // При какой скорости FOV максимальный
    public float maxFOVPenalty = 15f;  // На сколько градусов расширяем зрение
    public float shakeIntensity = 0.03f; // Сила тряски

    private float rotationX = 0f;
    private float rotationY = 0f;
    private Quaternion targetRotation;
    private Vector3 originalLocalPosition;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        targetRotation = transform.localRotation;
        originalLocalPosition = transform.localPosition;

        if (targetCamera == null) targetCamera = GetComponent<Camera>();
    }

    void Update()
    {
        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
        {
            float mouseX = Mouse.current.delta.x.ReadValue() * sensitivity * 0.1f;
            float mouseY = Mouse.current.delta.y.ReadValue() * sensitivity * 0.1f;

            rotationY += mouseX;
            rotationX -= mouseY;
            rotationX = Mathf.Clamp(rotationX, minYAngle, maxYAngle);

            targetRotation = Quaternion.Euler(rotationX, rotationY, 0);

            if (enableSmoothing)
                transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, smoothSpeed * Time.deltaTime);
            else
                transform.localRotation = targetRotation;
        }
    }

    void LateUpdate()
    {
        HandleKeyboardInputs();
        ApplyCameraEffects();
    }

    private void HandleKeyboardInputs()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[Key.R].wasPressedThisFrame)
        {
            rotationX = 0f;
            rotationY = 0f;
            targetRotation = Quaternion.Euler(0, 0, 0);
            transform.localRotation = targetRotation;
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

        // 1. Динамический FOV (чувство скорости)
        float currentSpeed = carRigidbody.linearVelocity.magnitude;
        float speedRatio = Mathf.Clamp01(currentSpeed / maxSpeedForFOV);
        float targetFOV = baseFOV + (maxFOVPenalty * speedRatio);
        targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, targetFOV, Time.deltaTime * 5f);

        // 2. Camera Shake (Тряска от скорости)
        if (currentSpeed > 1f)
        {
            Vector3 shakeOffset = Random.insideUnitSphere * shakeIntensity * speedRatio;
            transform.localPosition = Vector3.Lerp(transform.localPosition, originalLocalPosition + shakeOffset, Time.deltaTime * 10f);
        }
        else
        {
            transform.localPosition = Vector3.Lerp(transform.localPosition, originalLocalPosition, Time.deltaTime * 5f);
        }
    }
}