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

    private float rotationX = 0f;
    private float rotationY = 0f;
    private Quaternion targetRotation;

    void Start()
    {
        // Прячем и блокируем курсор по центру экрана
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        targetRotation = transform.localRotation;
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

            // Плавное вращение или моментальное (в зависимости от галочки в инспекторе)
            if (enableSmoothing)
            {
                transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, smoothSpeed * Time.deltaTime);
            }
            else
            {
                transform.localRotation = targetRotation;
            }
        }
    }

    void LateUpdate()
    {
        if (Keyboard.current == null) return;

        // Центрирование камеры
        if (Keyboard.current[Key.R].wasPressedThisFrame)
        {
            rotationX = 0f;
            rotationY = 0f;
            targetRotation = Quaternion.Euler(0, 0, 0);
            transform.localRotation = targetRotation;
        }

        // Разблокировка курсора для выхода или паузы
        if (Keyboard.current[Key.Escape].wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}