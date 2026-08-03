using UnityEngine;
using UnityEngine.InputSystem;

public class CarController : MonoBehaviour
{
    public enum DriveType { AWD, RWD, FWD }

    [Header("Двигатель и Трансмиссия")]
    public float motorForce = 15000f;
    public float brakeForce = 35000f;
    public float reverseForce = 8000f;
    public float maxSpeed = 35f;
    public float engineBraking = 2000f; // Торможение двигателем при отпущенном газе
    public DriveType driveType = DriveType.AWD; // Тип привода

    [Header("Управление и Повороты")]
    public float maxSteerAngle = 35f;
    public float steeringSpeed = 10f;
    public float turnSensitivity = 2.5f;
    [Range(0f, 1f)] public float speedSteerReduction = 0.65f; // Сильнее режем руль на скорости для стабильности

    [Header("Физика шин и Слайд (Drift)")]
    [Range(0f, 1f)] public float maxTireGrip = 0.95f;  // Идеальный держак
    [Range(0f, 1f)] public float minTireGrip = 0.4f;   // Держак в скольжении
    [Range(0f, 1f)] public float handbrakeGrip = 0.25f;
    public float airDrag = 1.2f;

    [Header("Имитация подвески и веса (1500 кг)")]
    public float bodyRollMultiplier = 2500f;
    public float pitchMultiplier = 4000f;
    public float downforce = 1000f; // Прижимная сила (важно для высоких скоростей)
    public float gravityMultiplier = 3f;
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.55f, 0f);

    [Header("Дождь и Лобовое стекло")]
    public Material windshieldMaterial;

    private Rigidbody rb;
    private float movementInput;
    private float rotationInput;
    private bool isHandbraking;
    private float currentSteerAngle;
    private float currentGrip; // Динамическое сцепление

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.mass = 1000f;
        rb.linearDamping = 0.1f;
        rb.angularDamping = 2.5f; // Машина плотнее сидит на дороге
        rb.centerOfMass = centerOfMassOffset;
        rb.constraints = RigidbodyConstraints.None;

        currentGrip = maxTireGrip;
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

        if (windshieldMaterial != null && rb != null)
        {
            float currentSpeed = transform.InverseTransformDirection(rb.linearVelocity).z;
            windshieldMaterial.SetFloat("_Speed", currentSpeed);
        }
    }

    void FixedUpdate()
    {
        Vector3 forwardVelocity = transform.forward * Vector3.Dot(rb.linearVelocity, transform.forward);
        Vector3 rightVelocity = transform.right * Vector3.Dot(rb.linearVelocity, transform.right);

        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float currentSpeedAbs = rb.linearVelocity.magnitude;

        CalculateDynamicGrip(currentSpeedAbs, forwardSpeed);

        ApplyMotorAndBrakes(forwardSpeed, forwardVelocity, currentSpeedAbs);
        ApplyTireFriction(rightVelocity);
        ApplySteering(forwardSpeed, currentSpeedAbs);
        ApplyWeightTransfer(forwardSpeed, currentSpeedAbs);

        // Аэродинамика и гравитация
        rb.AddForce(-forwardVelocity * (airDrag * currentSpeedAbs));
        rb.AddForce(-transform.up * (downforce * currentSpeedAbs), ForceMode.Force);
        rb.AddForce(Physics.gravity * rb.mass * (gravityMultiplier - 1f));
    }

    private void CalculateDynamicGrip(float speed, float forwardSpeed)
    {
        // Чем быстрее едем и резче поворачиваем, тем сильнее срывает заднюю ось
        float slipFactor = Mathf.Abs(rotationInput) * (speed / maxSpeed);

        // Для заднего привода занос при газе сильнее
        if (driveType == DriveType.RWD && movementInput > 0)
            slipFactor *= 1.3f;

        float targetGrip = Mathf.Lerp(maxTireGrip, minTireGrip, slipFactor);

        // Плавное возвращение сцепления
        currentGrip = Mathf.Lerp(currentGrip, targetGrip, Time.fixedDeltaTime * 5f);
    }

    private void ApplyMotorAndBrakes(float forwardSpeed, Vector3 forwardVelocity, float currentSpeedAbs)
    {
        if (movementInput != 0)
        {
            if (movementInput > 0)
            {
                if (forwardSpeed < -0.5f) // Тормозим
                    rb.AddForce(transform.forward * brakeForce, ForceMode.Force);
                else if (forwardSpeed < maxSpeed) // Разгоняемся
                {
                    float speedRatio = 1f - (forwardSpeed / maxSpeed);
                    rb.AddForce(transform.forward * (motorForce * speedRatio * movementInput), ForceMode.Force);
                }
            }
            else // Задний ход / Тормоз
            {
                if (forwardSpeed > 0.5f)
                    rb.AddForce(-transform.forward * brakeForce, ForceMode.Force);
                else if (forwardSpeed > -maxSpeed * 0.4f)
                    rb.AddForce(transform.forward * (reverseForce * movementInput), ForceMode.Force);
            }
        }
        else
        {
            // Торможение двигателем, если не жмем газ/тормоз
            if (currentSpeedAbs > 1f && !isHandbraking)
            {
                rb.AddForce(-forwardVelocity.normalized * engineBraking, ForceMode.Force);
            }
        }

        // Ручник и мертвая хватка при остановке
        if (isHandbraking)
        {
            if (Mathf.Abs(forwardSpeed) < 1f && currentSpeedAbs < 1f)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                Vector3 brakeDir = forwardVelocity.sqrMagnitude > 0.01f ? forwardVelocity.normalized : transform.forward;
                rb.AddForce(-brakeDir * (brakeForce * 0.8f), ForceMode.Force);
            }
        }
    }

    private void ApplyTireFriction(Vector3 rightVelocity)
    {
        // Боковой импульс теперь зависит от динамического грипа
        Vector3 impulse = -rightVelocity * (currentGrip * 15f * Time.fixedDeltaTime);
        rb.AddForce(impulse, ForceMode.VelocityChange);
    }

    private void ApplySteering(float forwardSpeed, float currentSpeedAbs)
    {
        if (currentSpeedAbs < 0.2f) return;

        float speedFactor = Mathf.Clamp01(currentSpeedAbs / maxSpeed);
        float targetSteerAngle = rotationInput * maxSteerAngle * (1f - (speedFactor * speedSteerReduction));

        if (forwardSpeed < -0.5f) targetSteerAngle *= -1f;

        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, steeringSpeed * Time.fixedDeltaTime);

        float turnDegreesPerSecond = currentSteerAngle * (Mathf.Abs(forwardSpeed) / maxSpeed) * turnSensitivity;
        Quaternion turnRotation = Quaternion.Euler(0f, turnDegreesPerSecond * Time.fixedDeltaTime * 10f, 0f);

        rb.MoveRotation(rb.rotation * turnRotation);
    }

    private void ApplyWeightTransfer(float forwardSpeed, float currentSpeedAbs)
    {
        float speedFactor = Mathf.Clamp01(currentSpeedAbs / maxSpeed);
        float rollTorque = -currentSteerAngle * speedFactor * bodyRollMultiplier;

        // Добавляем крен от потери сцепления (в заносе машину кренит сильнее)
        rollTorque *= (1f + (maxTireGrip - currentGrip));

        rb.AddRelativeTorque(Vector3.forward * rollTorque);

        if (forwardSpeed > 1f)
        {
            if (movementInput < 0 || isHandbraking)
                rb.AddRelativeTorque(Vector3.right * pitchMultiplier);
            else if (movementInput > 0)
                rb.AddRelativeTorque(Vector3.left * pitchMultiplier * 0.6f);
        }
    }
}