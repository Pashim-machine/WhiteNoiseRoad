using UnityEngine;
using UnityEngine.InputSystem;

public class CarController : MonoBehaviour
{
    [Header("Мощность и Масса (1500 кг)")]
    public float motorForce = 15000f;
    public float brakeForce = 35000f;
    public float reverseForce = 8000f;
    public float maxSpeed = 30f;

    [Header("Управление и Повороты")]
    public float maxSteerAngle = 35f;
    public float steeringSpeed = 10f;
    public float turnSensitivity = 2.5f;
    [Range(0f, 1f)]
    public float speedSteerReduction = 0.5f;

    [Header("Физика шин и Занос (Grip)")]
    [Range(0f, 1f)]
    public float tireGrip = 0.88f;
    [Range(0f, 1f)]
    public float handbrakeGrip = 0.2f;
    public float airDrag = 1.2f;

    [Header("Тяжесть и Аэродинамика")]
    public float downforce = 600f;
    public float gravityMultiplier = 3f; // Во сколько раз машина падает быстрее обычного
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.6f, 0f); // Смещение веса под днище

    [Header("Дождь и Лобовое стекло")]
    public Material windshieldMaterial; // Ссылка на материал стекла с эффектом дождя

    private Rigidbody rb;
    private float movementInput;
    private float rotationInput;
    private bool isHandbraking;
    private float currentSteerAngle;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.mass = 1500f;
        rb.linearDamping = 0.1f;
        // Немного снизили сопротивление вращению, чтобы машина могла быстро падать на колеса
        rb.angularDamping = 1.5f;

        // МАГИЯ ТЯЖЕСТИ 1: Смещаем центр масс вниз, чтобы машина всегда вставала на 4 колеса
        rb.centerOfMass = centerOfMassOffset;

        // МАГИЯ ТЯЖЕСТИ 2: Снимаем все блокировки осей! Теперь машина может кувыркаться при жестких ДТП, но вес будет возвращать её на место
        rb.constraints = RigidbodyConstraints.None;
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

        // Передаем локальную скорость машины в материал стекла для шейдера дождя
        if (windshieldMaterial != null && rb != null)
        {
            float currentSpeed = transform.InverseTransformDirection(rb.linearVelocity).z; // Положительная при движении вперед, отрицательная назад
            windshieldMaterial.SetFloat("_Speed", currentSpeed);
        }
    }

    void FixedUpdate()
    {
        Vector3 forwardVelocity = transform.forward * Vector3.Dot(rb.linearVelocity, transform.forward);
        Vector3 rightVelocity = transform.right * Vector3.Dot(rb.linearVelocity, transform.right);

        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float currentSpeedAbs = rb.linearVelocity.magnitude;

        ApplyMotorAndBrakes(forwardSpeed, forwardVelocity);
        ApplyTireFriction(rightVelocity);
        ApplySteering(forwardSpeed, currentSpeedAbs);

        // Прижимная сила и воздух
        rb.AddForce(-forwardVelocity * (airDrag * currentSpeedAbs));
        rb.AddForce(-transform.up * (downforce * currentSpeedAbs), ForceMode.Force);

        // МАГИЯ ТЯЖЕСТИ 3: Экстра-гравитация для устранения эффекта "перышка"
        rb.AddForce(Physics.gravity * rb.mass * (gravityMultiplier - 1f));
    }

    private void ApplyMotorAndBrakes(float forwardSpeed, Vector3 forwardVelocity)
    {
        if (movementInput > 0)
        {
            if (forwardSpeed < -0.5f)
                rb.AddForce(transform.forward * brakeForce, ForceMode.Force);
            else if (forwardSpeed < maxSpeed)
            {
                float speedRatio = 1f - (forwardSpeed / maxSpeed);
                rb.AddForce(transform.forward * (motorForce * speedRatio * movementInput), ForceMode.Force);
            }
        }
        else if (movementInput < 0)
        {
            if (forwardSpeed > 0.5f)
                rb.AddForce(-transform.forward * brakeForce, ForceMode.Force);
            else if (forwardSpeed > -maxSpeed * 0.35f)
                rb.AddForce(transform.forward * (reverseForce * movementInput), ForceMode.Force);
        }

        if (isHandbraking)
        {
            Vector3 brakeDir = forwardVelocity.sqrMagnitude > 0.01f ? forwardVelocity.normalized : transform.forward;
            rb.AddForce(-brakeDir * (brakeForce * 0.6f), ForceMode.Force);
        }
    }

    private void ApplyTireFriction(Vector3 rightVelocity)
    {
        float currentGrip = isHandbraking ? handbrakeGrip : tireGrip;
        Vector3 impulse = -rightVelocity * (currentGrip * 12f * Time.fixedDeltaTime);
        rb.AddForce(impulse, ForceMode.VelocityChange);
    }

    private void ApplySteering(float forwardSpeed, float currentSpeedAbs)
    {
        if (currentSpeedAbs < 0.2f) return;

        float speedFactor = Mathf.Clamp01(currentSpeedAbs / maxSpeed);
        float targetSteerAngle = rotationInput * maxSteerAngle * (1f - (speedFactor * speedSteerReduction));

        if (forwardSpeed < -0.5f) targetSteerAngle *= -1f;

        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, steeringSpeed * Time.fixedDeltaTime);

        float turnDegreesPerSecond = currentSteerAngle * (forwardSpeed / maxSpeed) * turnSensitivity;
        Quaternion turnRotation = Quaternion.Euler(0f, turnDegreesPerSecond * Time.fixedDeltaTime * 10f, 0f);

        rb.MoveRotation(rb.rotation * turnRotation);
    }
}