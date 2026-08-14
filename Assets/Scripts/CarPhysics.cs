using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(CarController))]
public class CarPhysics : MonoBehaviour
{
    [Header("Vehicle")]
    public float mass = 1050f;
    public Vector3 centerOfMassOffset = new(0f, -0.30f, -0.10f);
    public float angularDamping = 1.5f;
    public float dragCoefficient = 40f;

    [Header("Engine (FWD)")]
    public float maxEngineForce = 8000f;
    public float maxReverseForce = 4000f;
    public AnimationCurve forceBySpeed = new(
        new Keyframe(0f, 1f),
        new Keyframe(12f, 1f),
        new Keyframe(50f, 0f));

    [Header("Steering")]
    public float maxSteerAngle = 35f;
    public AnimationCurve steeringBySpeed = new(
        new Keyframe(0f, 1f),
        new Keyframe(20f, 0.85f),
        new Keyframe(40f, 0.55f),
        new Keyframe(55f, 0.35f));
    public float ackermannFactor = 0.25f;

    [Header("Brakes")]
    public float brakeForce = 3200f;
    public float handbrakeForce = 5200f;
    public float handbrakeGripFactor = 0.4f;

    [Header("Tires")]
    public float frontGrip = 1.00f;
    public float rearGrip = 0.88f;
    public AnimationCurve longitudinalGripCurve = new(
        new Keyframe(0f, 1f),
        new Keyframe(0.15f, 1f),
        new Keyframe(0.4f, 0.85f),
        new Keyframe(1f, 0.7f));
    public AnimationCurve lateralGripCurve = new(
        new Keyframe(0f, 0.85f),
        new Keyframe(1.5f, 0.95f),
        new Keyframe(3f, 1f),
        new Keyframe(7f, 0.75f),
        new Keyframe(12f, 0.6f));
    public float tireStiffness = 12000f;
    public float corneringStiffness = 1500f;
    public float rollingResistance = 150f;
    public float wheelInertia = 2f;

    [Header("Suspension")]
    public float wheelRadius = 0.30f;
    public float suspensionTravel = 0.20f;
    public float springStrength = 30000f;
    public float damperStrength = 4500f;

    [Header("Anti Roll")]
    public float frontAntiRoll = 9000f;
    public float rearAntiRoll = 7000f;

    [Header("Aero")]
    public AnimationCurve downforceCurve = new(
        new Keyframe(0f, 0f),
        new Keyframe(15f, 200f),
        new Keyframe(30f, 800f),
        new Keyframe(50f, 1800f));

    [Header("Stabilization")]
    public float yawRateDamper = 400f;
    public float antiRolloverTorque = 2500f;
    public float maxStableRollDeg = 30f;
    [Tooltip("Гашение раскачки в крене (повороты). N*m*s: больше = кузов быстрее успокаивается")]
    public float rollRateDamper = 3000f;
    [Tooltip("Гашение клевков при разгоне/торможении")]
    public float pitchRateDamper = 2000f;

    [Header("Wheels")]
    [Tooltip("Ось качения в локальных координатах модели колеса. Обычно X; если крутится неправильно — попробуй (0,0,1)")]
    public Vector3 wheelSpinAxis = new(1f, 0f, 0f);
    [Tooltip("Знак визуального вращения колёс: если крутятся назад при газе — поменяй")]
    public float wheelSpinDirection = -1f;

    [Header("Wheels: маунты (опционально)")]
    public Transform frontLeftMount;
    public Transform frontRightMount;
    public Transform rearLeftMount;
    public Transform rearRightMount;

    [Header("Wheels: визуалы (обязательно)")]
    public Transform frontLeftVisual;
    public Transform frontRightVisual;
    public Transform rearLeftVisual;
    public Transform rearRightVisual;

    [Header("Ground")]
    public LayerMask groundMask = ~0;

    [Header("Debug")]
    public bool debugWheels = true;
    public bool debugLog = false;

    private Rigidbody rb;
    private CarController input;
    private CarWheel FL, FR, RL, RR;
    private float logTimer;

    public Vector3 GetVelocity() => rb != null ? rb.linearVelocity : Vector3.zero;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<CarController>();

        rb.mass = mass;
        rb.centerOfMass = centerOfMassOffset;
        rb.angularDamping = angularDamping;
        rb.linearDamping = 0f;
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxAngularVelocity = 12f;
        rb.sleepThreshold = 0.005f;

        // Жёсткий сброс параметров, которые Unity любит перезаписывать из Inspector
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.None;
        rb.linearDamping = 0f;
        rb.angularDamping = angularDamping;
        rb.maxDepenetrationVelocity = 10f;

        // Диагностический лог при старте — сразу видно, если что-то не так
        Debug.Log($"[CarPhysics] mass={rb.mass} drag={rb.linearDamping} kinematic={rb.isKinematic} " +
                  $"constraints={rb.constraints} gravity={rb.useGravity}", this);
    }

    void Start()
    {
        float staticComp = (mass * 9.81f / 4f) / springStrength;

        FL = CreateWheel(frontLeftMount, frontLeftVisual, true, true, false, frontGrip, staticComp);
        FR = CreateWheel(frontRightMount, frontRightVisual, true, true, false, frontGrip, staticComp);
        RL = CreateWheel(rearLeftMount, rearLeftVisual, false, false, true, rearGrip, staticComp);
        RR = CreateWheel(rearRightMount, rearRightVisual, false, false, true, rearGrip, staticComp);

        // FIX IDE0031
        FL?.CalibrateRest(transform, wheelRadius, staticComp, groundMask);
        FR?.CalibrateRest(transform, wheelRadius, staticComp, groundMask);
        RL?.CalibrateRest(transform, wheelRadius, staticComp, groundMask);
        RR?.CalibrateRest(transform, wheelRadius, staticComp, groundMask);
    }

    CarWheel CreateWheel(Transform mount, Transform visual, bool steering, bool driving,
            bool handbrakeAxle, float grip, float staticComp)
    {
        if (mount == null && visual == null)
        {
            Debug.LogError($"[CarPhysics] На '{name}' не назначено колесо (ни mount, ни visual)!", this);
            return null;
        }
        CarWheel w = new CarWheel
        {
            mountPoint = mount,
            visualTransform = visual,
            rb = rb,
            isSteering = steering,
            isDriving = driving,
            isHandbrakeAxle = handbrakeAxle,
            gripMultiplier = grip,
            lateralGripCurve = lateralGripCurve,
            longitudinalGripCurve = longitudinalGripCurve,
            spinAxis = wheelSpinAxis,
            spinDirection = wheelSpinDirection,
        };
        w.InitAnchor(transform, 0f, staticComp);
        return w;
    }

    void FixedUpdate()
    {
        if (debugLog)
        {
            logTimer += Time.fixedDeltaTime;
            if (logTimer >= 1f)
            {
                logTimer = 0f;
                float spd = rb.linearVelocity.magnitude;
                Debug.Log($"[Car] thr={input.throttle:F2} brk={input.brake:F2} steer={input.steerTarget:F2} " +
                          $"spd={spd:F1} curve={forceBySpeed.Evaluate(spd):F2} " +
                          $"G={(FL != null && FL.isGrounded ? 1 : 0)}{(FR != null && FR.isGrounded ? 1 : 0)}" +
                          $"{(RL != null && RL.isGrounded ? 1 : 0)}{(RR != null && RR.isGrounded ? 1 : 0)}");
            }
        }

        if (FL == null || FR == null || RL == null || RR == null) return;

        float dt = Time.fixedDeltaTime;
        float speed = rb.linearVelocity.magnitude;

        float throttle = input.throttle;
        float brakeIn = input.brake;
        float steerIn = input.steerTarget;
        bool handbrake = input.handbrake;

        // Руль: скорость-зависимый + Ackermann
        float speedFactor = steeringBySpeed.Evaluate(speed);
        float baseAngle = steerIn * maxSteerAngle * speedFactor;
        float innerMul = 1f + ackermannFactor * Mathf.Abs(steerIn);
        float outerMul = 1f - ackermannFactor * 0.3f * Mathf.Abs(steerIn);

        FL.steerAngleDeg = baseAngle * (steerIn >= 0f ? outerMul : innerMul);
        FR.steerAngleDeg = baseAngle * (steerIn >= 0f ? innerMul : outerMul);
        RL.steerAngleDeg = 0f;
        RR.steerAngleDeg = 0f;

        // Тяга FWD
        float forceCurve = forceBySpeed.Evaluate(speed);
        float engineForce = throttle > 0f ? maxEngineForce * forceCurve
                          : throttle < 0f ? -maxReverseForce : 0f;

        FL.driveTorque = engineForce * 0.5f * wheelRadius;
        FR.driveTorque = engineForce * 0.5f * wheelRadius;
        RL.driveTorque = 0f;
        RR.driveTorque = 0f;

        // Сервисный тормоз на все 4
        float service = brakeIn * brakeForce * 0.25f;
        FL.brakeTorque = service * wheelRadius;
        FR.brakeTorque = service * wheelRadius;
        RL.brakeTorque = service * wheelRadius;
        RR.brakeTorque = service * wheelRadius;

        // Ручник: зад + ослабление бокового сцепления зада
        float hb = handbrake ? handbrakeForce * 0.5f * wheelRadius : 0f;
        FL.handbrakeTorque = 0f;
        FR.handbrakeTorque = 0f;
        RL.handbrakeTorque = hb;
        RR.handbrakeTorque = hb;

        CarWheel.Params wp = new CarWheel.Params
        {
            wheelRadius = wheelRadius,
            suspensionTravel = suspensionTravel,
            springStrength = springStrength,
            damperStrength = damperStrength,
            wheelInertia = wheelInertia,
            tireStiffness = tireStiffness,
            corneringStiffness = corneringStiffness,
            rollingResistance = rollingResistance,
            handbrakeGripFactor = handbrakeGripFactor,
            isHandbraking = handbrake,
            staticLoad = mass * 9.81f / 4f,
            groundMask = groundMask
        };

        FL.Evaluate(in wp, dt, transform);
        FR.Evaluate(in wp, dt, transform);
        RL.Evaluate(in wp, dt, transform);
        RR.Evaluate(in wp, dt, transform);

        ApplyAntiRoll(FL, FR, frontAntiRoll);
        ApplyAntiRoll(RL, RR, rearAntiRoll);

        float downforce = downforceCurve.Evaluate(speed);
        if (downforce > 0f)
            rb.AddForce(-transform.up * downforce, ForceMode.Force);

        Vector3 flatVel = rb.linearVelocity;
        flatVel.y = 0f;
        rb.AddForce(-flatVel * dragCoefficient, ForceMode.Force);

        Stabilize();
    }

    void ApplyAntiRoll(CarWheel left, CarWheel right, float barStiffness)
    {
        if (!left.isGrounded || !right.isGrounded) return;
        float delta = left.suspensionCompression - right.suspensionCompression;
        Vector3 up = -transform.up;
        rb.AddForceAtPosition(-up * delta * barStiffness, left.hitPoint, ForceMode.Force);
        rb.AddForceAtPosition(up * delta * barStiffness, right.hitPoint, ForceMode.Force);
    }

    void Stabilize()
    {
        Vector3 localAngVel = transform.InverseTransformDirection(rb.angularVelocity);

        // Демпфер рысканья
        rb.AddRelativeTorque(0f, -localAngVel.y * yawRateDamper, 0f, ForceMode.Force);

        // FIX UNT0024: Vector3 overload вместо скалярного
        rb.AddRelativeTorque(new Vector3(
            -localAngVel.x * pitchRateDamper,
            0f,
            -localAngVel.z * rollRateDamper), ForceMode.Force);

        float roll = Vector3.SignedAngle(transform.up, Vector3.up, transform.forward);
        if (Mathf.Abs(roll) > maxStableRollDeg)
        {
            float excess = Mathf.Abs(roll) - maxStableRollDeg;
            rb.AddRelativeTorque(0f, 0f, -Mathf.Sign(roll) * excess * antiRolloverTorque * 0.1f, ForceMode.Force);
        }
    }

    void LateUpdate()
    {
        // FIX IDE0031 & обновленная сигнатура ApplyVisual
        FL?.ApplyVisual();
        FR?.ApplyVisual();
        RL?.ApplyVisual();
        RR?.ApplyVisual();
    }

    void OnDrawGizmosSelected()
    {
        if (!debugWheels) return;
        // FIX IDE0031
        FL?.DrawGizmos(wheelRadius, transform);
        FR?.DrawGizmos(wheelRadius, transform);
        RL?.DrawGizmos(wheelRadius, transform);
        RR?.DrawGizmos(wheelRadius, transform);
    }
}