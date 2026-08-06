using UnityEngine;
using UnityEngine.InputSystem;

public class CarController : MonoBehaviour
{
    public enum DriveType { AWD, RWD, FWD }

    [Header("Двигатель и Тормоза")]
    // Для массы 1050 кг 15000-18000 Н даст нормальное ускорение (около 4-5 сек до сотни)
    public float motorForce = 15000f;
    public float brakeForce = 25000f;
    public float maxSpeed = 50f; // 50 м/с = 180 км/ч (реалистично для ВАЗ)
    public float engineBraking = 1500f;

    [Header("Управление (Аркадное)")]
    public float maxSteerAngle = 35f;
    public float steerInputSpeed = 5f;
    public float steerReturnSpeed = 8f;
    public float turnSpeed = 2.5f;
    [Range(0f, 1f)] public float speedSteerReduction = 0.5f;

    [Header("Сцепление и Дрифт")]
    [Range(0.8f, 1f)] public float tireGripFactor = 0.98f;
    public float handbrakeGripFactor = 0.4f;
    public float driftGrip = 0.92f;

    [Header("Физика кузова")]
    public float downforce = 1500f; // Уменьшили прижимную, иначе она вдавит машину в землю
    public float gravityMultiplier = 1f; // 1 = реальная гравитация Земли
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.5f, 0f);

    [Header("Визуальные Колеса (Пустые объекты с джойнтами)")]
    public Transform frontLeftWheelModel;
    public Transform frontRightWheelModel;

    private Rigidbody rb;
    private float movementInput;
    private float rotationInput;
    private bool isHandbraking;
    private float currentSteerInput = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMassOffset;
    }

    void Update()
    {
        if (Keyboard.current == null) return;

        movementInput = 0f;
        rotationInput = 0f;
        isHandbraking = Keyboard.current[Key.Space].isPressed;

        if (Keyboard.current[Key.W].isPressed) movementInput = 1f;
        if (Keyboard.current[Key.S].isPressed) movementInput = -1f;
        if (Keyboard.current[Key.A].isPressed) rotationInput = -1f;
        if (Keyboard.current[Key.D].isPressed) rotationInput = 1f;

        if (rotationInput != 0)
            currentSteerInput = Mathf.MoveTowards(currentSteerInput, rotationInput, steerInputSpeed * Time.deltaTime);
        else
            currentSteerInput = Mathf.MoveTowards(currentSteerInput, 0f, steerReturnSpeed * Time.deltaTime);
    }

    void FixedUpdate()
    {
        Vector3 localVel = transform.InverseTransformDirection(rb.linearVelocity);
        float forwardSpeed = localVel.z;
        float speed = rb.linearVelocity.magnitude;

        ApplyMotor(forwardSpeed, speed);
        ApplyArcadeSteering(forwardSpeed, speed);
        ApplyGrip(localVel);
        ApplyDownforce(speed);
    }

    private void ApplyMotor(float forwardSpeed, float speed)
    {
        if (movementInput != 0 && !isHandbraking)
        {
            if (movementInput > 0)
            {
                if (forwardSpeed < -0.5f)
                    rb.AddForce(transform.forward * brakeForce, ForceMode.Acceleration);
                else if (speed < maxSpeed)
                    rb.AddForce(transform.forward * motorForce, ForceMode.Acceleration);
            }
            else
            {
                if (forwardSpeed > 0.5f)
                    rb.AddForce(-transform.forward * brakeForce, ForceMode.Acceleration);
                else
                    rb.AddForce(-transform.forward * motorForce * 0.5f, ForceMode.Acceleration);
            }
        }
        else if (!isHandbraking)
        {
            // Двигатель тормозит, если не жмем газ и не ручник
            if (speed > 1f)
                rb.AddForce(-transform.forward * engineBraking, ForceMode.Acceleration);
        }

        // РУЧНИК / ТОРМОЗ ПРОБЕЛОМ
        if (isHandbraking)
        {
            if (speed < 3f) // Подняли порог: если скорость упала ниже 3 м/с, машина мгновенно вмерзает
            {
                rb.linearVelocity = Vector3.zero;
            }
            else
            {
                // Тормозим против РЕАЛЬНОГО вектора движения (а не просто вперед/назад).
                // Это глушит заносы и дрифт, позволяя машине быстро остановиться.
                Vector3 brakeDir = rb.linearVelocity.normalized;
                rb.AddForce(-brakeDir * (brakeForce * 1.5f), ForceMode.Acceleration);
            }
        }
    }

    private void ApplyArcadeSteering(float forwardSpeed, float speed)
    {
        float visualAngle = currentSteerInput * maxSteerAngle;
        UpdateVisualWheels(visualAngle);

        if (speed < 0.1f) return;

        float speedFactor = Mathf.Clamp01(speed / maxSpeed);
        float targetSteer = visualAngle * (1f - (speedFactor * speedSteerReduction));

        // ИСПРАВЛЕНО: Убрали двойную инверсию. 
        // Теперь поворот всегда следует за направлением движения.
        // Если едем вперед (знак +) - рулим нормально.
        // Если едем назад (знак -) - знак меняется один раз, и машина правильно поворачивает задом.
        float turnAmount = targetSteer * turnSpeed * Time.fixedDeltaTime * Mathf.Sign(forwardSpeed);

        Quaternion turnDelta = Quaternion.Euler(0f, turnAmount, 0f);
        rb.MoveRotation(rb.rotation * turnDelta);
    }

    private void ApplyGrip(Vector3 localVel)
    {
        // Если жмем ручник (пробел) и скорость упала ниже 5 м/с
        if (isHandbraking && rb.linearVelocity.magnitude < 5f)
        {
            // Жестко гасим скорость на 80% каждый кадр, пока не встанет на ноль
            rb.linearVelocity *= 0.8f;

            // Если скорость стала совсем ничтожной - обнуляем полностью
            if (rb.linearVelocity.magnitude < 0.5f)
            {
                rb.linearVelocity = Vector3.zero;
            }
            return; // Выходим из метода, чтобы код ниже не вернул скорость обратно
        }

        float currentGrip = tireGripFactor;
        if (isHandbraking) currentGrip = handbrakeGripFactor;
        else if (Mathf.Abs(localVel.x) > 3f) currentGrip = driftGrip;

        localVel.x *= (1f - currentGrip);
        rb.linearVelocity = transform.TransformDirection(localVel);
    }

    private void ApplyDownforce(float speed)
    {
        float downforceForce = Mathf.Clamp01(speed / 10f) * downforce;
        rb.AddForce(-transform.up * downforceForce, ForceMode.Force);
        rb.AddForce(Physics.gravity * (gravityMultiplier - 1f), ForceMode.Acceleration);
    }

    private void UpdateVisualWheels(float angle)
    {
        if (frontLeftWheelModel != null)
        {
            Vector3 euler = frontLeftWheelModel.localEulerAngles;
            frontLeftWheelModel.localEulerAngles = new Vector3(euler.x, angle, euler.z);
        }
        if (frontRightWheelModel != null)
        {
            Vector3 euler = frontRightWheelModel.localEulerAngles;
            frontRightWheelModel.localEulerAngles = new Vector3(euler.x, angle, euler.z);
        }
    }
}