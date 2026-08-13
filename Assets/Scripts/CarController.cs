using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CarPhysics))]
public class CarController : MonoBehaviour
{
    [Header("Чувствительность ввода")]
    public float steerResponse = 4.5f;
    public float steerReturnSpeed = 7f;

    internal float throttle;
    internal float brake;
    internal float steerTarget;
    internal bool handbrake;

    private CarPhysics carPhysics;
    private float steerCurrent;

    void Awake()
    {
        carPhysics = GetComponent<CarPhysics>();
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        throttle = 0f;
        brake = 0f;
        if (kb.wKey.isPressed) throttle = 1f;
        if (kb.sKey.isPressed)
        {
            float fwd = Vector3.Dot(carPhysics.GetVelocity(), transform.forward);
            if (fwd > 0.5f) brake = 1f;
            else throttle = -1f;
        }

        float target = 0f;
        if (kb.aKey.isPressed) target -= 1f;
        if (kb.dKey.isPressed) target += 1f;

        if (target != 0f)
            steerCurrent = Mathf.MoveTowards(steerCurrent, target, steerResponse * Time.deltaTime);
        else
            steerCurrent = Mathf.MoveTowards(steerCurrent, 0f, steerReturnSpeed * Time.deltaTime);

        steerTarget = steerCurrent;
        handbrake = kb.spaceKey.isPressed;
    }
}