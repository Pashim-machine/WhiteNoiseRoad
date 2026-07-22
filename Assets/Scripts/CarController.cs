using UnityEngine;
using UnityEngine.InputSystem;

public class CarController : MonoBehaviour
{
    [Header("Настройки езды")]
    public float maxSpeed = 20f;
    public float acceleration = 10f;
    public float deceleration = 15f;
    public float turnSpeed = 100f;

    private Rigidbody rb;
    private float currentSpeed = 0f;
    private float movementInput;
    private float rotationInput;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.mass = 1500;
        // Оставляем заморозку осей, если дорога всегда плоская.
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    void Update()
    {
        if (Keyboard.current == null) return;

        movementInput = 0f;
        rotationInput = 0f;

        if (Keyboard.current[Key.W].isPressed) movementInput = 1f;
        if (Keyboard.current[Key.S].isPressed) movementInput = -1f;
        if (Keyboard.current[Key.A].isPressed) rotationInput = -1f;
        if (Keyboard.current[Key.D].isPressed) rotationInput = 1f;
    }

    void FixedUpdate()
    {
        // 1. Плавный разгон и торможение
        float targetSpeed = movementInput * maxSpeed;
        float accelRate = (Mathf.Abs(movementInput) > 0) ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accelRate * Time.fixedDeltaTime);

        // 2. Движение через физическую скорость (в Unity 6 используем linearVelocity)
        Vector3 newVelocity = transform.forward * currentSpeed;
        newVelocity.y = rb.linearVelocity.y; // Сохраняем гравитацию для падений
        rb.linearVelocity = newVelocity;

        // 3. Поворот (с учетом направления движения)
        if (Mathf.Abs(currentSpeed) > 0.1f)
        {
            // Если едем назад (currentSpeed < 0), руль крутится в логичную сторону
            float directionMultiplier = Mathf.Sign(currentSpeed);
            float rotation = rotationInput * turnSpeed * directionMultiplier * Time.fixedDeltaTime;

            Quaternion turnOffset = Quaternion.Euler(0f, rotation, 0f);
            rb.MoveRotation(rb.rotation * turnOffset);
        }
    }
}