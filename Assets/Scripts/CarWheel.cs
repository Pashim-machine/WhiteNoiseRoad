using UnityEngine;

/// Не MonoBehaviour. Один экземпляр на колесо, создаётся в CarPhysics.Start.
public class CarWheel
{
    public Transform mountPoint;      // опционально
    public Transform visualTransform;
    public Rigidbody rb;

    public Vector3 spinAxis = Vector3.right;
    public float spinDirection = -1f;   // если колёса крутятся назад при газе — поменяй знак
    private Vector3 homeLocal;          // родная позиция колеса в ЛОКАЛЕ его родителя
    private float staticComp;
    private float anchorHeight;

    public bool isSteering;
    public bool isDriving;
    public bool isHandbrakeAxle;
    public float gripMultiplier = 1f;

    public AnimationCurve lateralGripCurve;
    public AnimationCurve longitudinalGripCurve;

    // Команды на текущий FixedUpdate
    public float steerAngleDeg;
    public float driveTorque;
    public float brakeTorque;
    public float handbrakeTorque;

    // Состояние
    public bool isGrounded;
    public Vector3 hitPoint;
    public Vector3 hitNormal;
    public float suspensionCompression;
    public float wheelAngularVelocity;
    public float restLength = 0.35f;

    private Vector3 anchorLocal;
    private float prevCompression;
    private float rollDeg;
    private Quaternion baseLocalRotation = Quaternion.identity;
    private bool baseCaptured;

    public struct Params
    {
        public float wheelRadius;
        public float suspensionTravel;
        public float springStrength;
        public float damperStrength;
        public float wheelInertia;
        public float tireStiffness;
        public float corneringStiffness;
        public float rollingResistance;
        public float handbrakeGripFactor;
        public bool isHandbraking;
        public float staticLoad;
        public LayerMask groundMask;
    }

    public void InitAnchor(Transform chassis, float height, float staticCompression)
    {
        anchorHeight = height;
        staticComp = staticCompression;
        if (visualTransform != null)
        {
            // Якорь луча — в координатах кузова (для физики)
            anchorLocal = chassis.InverseTransformPoint(visualTransform.position)
                          + chassis.InverseTransformDirection(Vector3.up) * height;
        }
    }

    Vector3 GetOrigin(Transform chassis)
    {
        if (mountPoint != null) return mountPoint.position;
        return chassis.TransformPoint(anchorLocal);
    }

    /// Автокалибровка длины подвески по реальной высоте до земли.
    public void CalibrateRest(Transform chassis, float wheelRadius, float staticComp, LayerMask mask)
    {
        Vector3 origin = GetOrigin(chassis);
        Vector3 dir = -chassis.up;
        Vector3 start = origin;
        float traveled = 0f;
        float remaining = 3f;

        while (remaining > 0.01f && Physics.Raycast(start, dir, out RaycastHit hit, remaining, mask, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(chassis))
            {
                float groundDist = traveled + hit.distance;
                restLength = Mathf.Clamp(groundDist - wheelRadius + staticComp, 0.05f, 1.5f);
                return;
            }
            float used = hit.distance + 0.01f;
            traveled += used;
            start += dir * used;
            remaining -= used;
        }
    }

    public void Evaluate(in Params p, float dt, Transform chassis)
    {
        isGrounded = false;
        suspensionCompression = 0f;
        if (rb == null || (visualTransform == null && mountPoint == null)) return;

        Vector3 origin = GetOrigin(chassis);
        Vector3 suspDir = -chassis.up;
        float maxDist = Mathf.Max(restLength, 0.45f) + p.wheelRadius + 0.25f;

        bool grounded = false;
        Vector3 start = origin;
        float remaining = maxDist;

        while (remaining > 0.01f && Physics.Raycast(start, suspDir, out RaycastHit hit, remaining, p.groundMask, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(chassis))
            {
                grounded = true;
                hitPoint = hit.point;
                hitNormal = hit.normal;
                break;
            }
            float used = hit.distance + 0.01f;
            start += suspDir * used;
            remaining -= used;
        }

        if (!grounded)
        {
            // В воздухе: колесо свободно раскручивается
            float hubDrag = Mathf.Sign(wheelAngularVelocity) * 5f;
            wheelAngularVelocity += (driveTorque - hubDrag) / p.wheelInertia * dt;
            wheelAngularVelocity = Mathf.Clamp(wheelAngularVelocity, -250f, 250f);
            prevCompression = 0f;
            return;
        }

        isGrounded = true;

        // ---------- Подвеска ----------
        float dist = Vector3.Dot(hitPoint - origin, suspDir);
        float compression = Mathf.Clamp(restLength - (dist - p.wheelRadius), 0f, p.suspensionTravel);
        suspensionCompression = compression;

        float suspVel = (compression - prevCompression) / Mathf.Max(dt, 1e-4f);
        prevCompression = compression;

        float forceMag = compression * p.springStrength + suspVel * p.damperStrength;
        if (forceMag < 0f) forceMag = 0f;
        Vector3 forceDir = Vector3.Normalize(-suspDir * 0.75f + hitNormal * 0.25f);
        rb.AddForceAtPosition(forceDir * forceMag, hitPoint, ForceMode.Force);

        // ---------- Шина ----------
        Quaternion steerRot = Quaternion.AngleAxis(steerAngleDeg, chassis.up);
        Vector3 wheelForward = steerRot * chassis.forward;
        Vector3 wheelRight = steerRot * chassis.right;

        Vector3 pointVel = rb.GetPointVelocity(hitPoint);
        float forwardSpeed = Vector3.Dot(pointVel, wheelForward);
        float lateralSpeed = Vector3.Dot(pointVel, wheelRight);

        float normalLoad = Mathf.Lerp(compression * p.springStrength, p.staticLoad, 0.35f);
        normalLoad = Mathf.Max(200f, normalLoad);
        float maxTireForce = normalLoad * 1.8f;

        // 1) Моменты крутят колесо
        float brakeSum = brakeTorque + handbrakeTorque;
        float appliedTorque = driveTorque - Mathf.Sign(wheelAngularVelocity) * (brakeSum + 5f);
        wheelAngularVelocity += appliedTorque / p.wheelInertia * dt;

        // 2) Настоящий slip ratio
        float surfaceSpeed = wheelAngularVelocity * p.wheelRadius;
        float denom = Mathf.Max(1f, Mathf.Abs(surfaceSpeed), Mathf.Abs(forwardSpeed));
        float slipRatio = Mathf.Clamp((surfaceSpeed - forwardSpeed) / denom, -2f, 2f);

        float longGrip = longitudinalGripCurve != null
            ? longitudinalGripCurve.Evaluate(Mathf.Abs(slipRatio)) : 1f;

        // 3) Продольная сила, ограниченная нагрузкой
        float tireForce = Mathf.Clamp(slipRatio * p.tireStiffness * longGrip, -maxTireForce, maxTireForce);

        // 4) Реакция без перескока через скорость земли (стабильность)
        float groundSpin = forwardSpeed / p.wheelRadius;
        float currentDelta = wheelAngularVelocity - groundSpin;
        float reactionDelta = (tireForce * p.wheelRadius / p.wheelInertia) * dt;
        if (currentDelta > 0f) reactionDelta = Mathf.Clamp(reactionDelta, 0f, currentDelta);
        else reactionDelta = Mathf.Clamp(reactionDelta, currentDelta, 0f);
        wheelAngularVelocity -= reactionDelta;
        wheelAngularVelocity = Mathf.Clamp(wheelAngularVelocity, -250f, 250f);

        // 5) Боковая сила
        float latGrip = (lateralGripCurve != null
            ? lateralGripCurve.Evaluate(Mathf.Abs(lateralSpeed)) : 1f) * gripMultiplier;
        if (p.isHandbraking && isHandbrakeAxle) latGrip *= p.handbrakeGripFactor;
        float lateralForce = Mathf.Clamp(-lateralSpeed * latGrip * p.corneringStiffness, -maxTireForce, maxTireForce);

        // 6) Сопротивление качению
        float rollForce = -Mathf.Sign(forwardSpeed) * p.rollingResistance
                          * Mathf.Clamp01(Mathf.Abs(forwardSpeed) * 2f);

        Vector3 totalForce = wheelForward * (tireForce + rollForce) + wheelRight * lateralForce;
        rb.AddForceAtPosition(totalForce, hitPoint, ForceMode.Force);
    }

    public void ApplyVisual()
    {
        if (visualTransform == null) return;

        // FIX: запоминаем родной localPosition/localRotation колеса
        // относительно ЕГО родителя (PlayerCar), а не CarRoot —
        // иначе колёса уезжали внутрь кузова и становились «чёрными».
        if (!baseCaptured)
        {
            baseLocalRotation = visualTransform.localRotation;
            homeLocal = visualTransform.localPosition;
            baseCaptured = true;
        }

        rollDeg += wheelAngularVelocity * spinDirection * Mathf.Rad2Deg * Time.deltaTime;

        // X/Z жёстко в локале родителя (не отстают от машины на скорости),
        // Y следует за подвеской: вверх при сжатии, вниз при отбое.
        Vector3 local = homeLocal;
        local.y += suspensionCompression - staticComp;

        Quaternion rot = Quaternion.Euler(0f, steerAngleDeg, 0f) *
                         baseLocalRotation *
                         Quaternion.AngleAxis(rollDeg, spinAxis);

        visualTransform.SetLocalPositionAndRotation(local, rot);
    }

    public void DrawGizmos(float wheelRadius, Transform chassis)
    {
        Vector3 origin = GetOrigin(chassis);
        Vector3 suspDir = -chassis.up;
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawLine(origin, origin + suspDir * (suspensionCompression + wheelRadius));
        if (isGrounded)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(hitPoint, 0.08f);
        }
    }
}