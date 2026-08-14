using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class HDRPWeatherManager : MonoBehaviour
{
    [Header("Настройки времени суток")]
    public float dayLengthInRealSeconds = 300f;
    [Range(0f, 24f)]
    public float currentTimeOfDay = 8f;

    [Header("Светила (Направленные источники)")]
    public Light sunLight;
    public Light moonLight;
    public float sunBaseLux = 100000f;
    public float moonBaseLux = 10000f;
    public float rainSunLux = 15000f;

    [Header("Орбита светил")]
    [Range(10f, 85f)] public float maxSunAltitude = 65f;   // высота солнца в полдень
    [Range(10f, 85f)] public float maxMoonAltitude = 55f;  // высота луны в полночь
    [Range(0f, 360f)] public float orbitYawOffset = 0f;    // развернуть восход/закат под карту

    [Header("Цвета неба и света")]
    public Color daySunColor = new(1f, 0.95f, 0.8f);
    public Color sunsetSunColor = new(1f, 0.5f, 0.2f);
    public Color moonColor = new(0.6f, 0.7f, 1f);

    [Header("Настройки погоды")]
    [Range(0f, 100f)]
    public float rainChancePercent = 10f;
    public float weatherCheckInterval = 30f;
    public float rainDurationMinutes = 20f;
    public float volumeTransitionSpeed = 0.5f;

    [Header("Ссылки на объекты")]
    public ParticleSystem rainParticles;
    public AudioSource rainAudio;
    public Volume weatherVolume;
    public VolumeProfile clearProfile;
    public VolumeProfile rainProfile;

    [Header("Материал лобового стекла")]
    public Material windshieldMaterial;
    public float rainFadeSpeed = 0.5f;
    private float currentWindshieldRain = 0f;

    private bool isRaining = false;
    private float weatherTimer = 0f;
    private float rainTimer = 0f;
    private float currentVolumeBlend = 0f;

    void Start()
    {
        SetRain(false);
        if (windshieldMaterial != null)
            windshieldMaterial.SetFloat("_Rain_Amount", 0f);

        // КРИТИЧНО: Настройка теней для Directional Lights
        SetupLightShadows(sunLight);
        SetupLightShadows(moonLight);

        // Луна всегда активна, просто с нулевой интенсивностью
        if (moonLight != null)
        {
            moonLight.gameObject.SetActive(true);
            moonLight.intensity = 0f;
        }
    }

    void SetupLightShadows(Light light)
    {
        if (light == null) return;

        light.shadows = LightShadows.Soft;
        light.shadowNearPlane = 0.1f;
        light.shadowStrength = 1f;

        // HDRP-разрешение теней ставится В ИНСПЕКТОРЕ: Light → Shadows → Resolution = Ultra.
        // Программно в Unity 6 оно read-only, поэтому тут только базовые вещи.
    }

    void Update()
    {
        UpdateTimeOfDay();
        UpdateWeatherSystem();
        UpdateVolumeTransition();
        UpdateWindshieldRain();
    }

    void UpdateTimeOfDay()
    {
        float hoursPerSecond = 24f / dayLengthInRealSeconds;
        currentTimeOfDay += hoursPerSecond * Time.deltaTime;
        if (currentTimeOfDay >= 24f) currentTimeOfDay -= 24f;

        // Луна идёт в противофазе (+12 часов)
        UpdateCelestial(sunLight, currentTimeOfDay, maxSunAltitude, isRaining ? rainSunLux : sunBaseLux, true);
        UpdateCelestial(moonLight, currentTimeOfDay + 12f, maxMoonAltitude, moonBaseLux, false);
    }

    /// Позиция светила по азимуту + высоте, как на настоящей небесной сфере
    void UpdateCelestial(Light light, float bodyTime, float maxAltitude, float baseLux, bool isSun)
    {
        if (light == null) return;
        bodyTime %= 24f;

        // Высота: 0 на восходе/закате (6ч/18ч), +max в полдень, -max в полночь — гладкая синусоида
        float altDeg = Mathf.Sin(((bodyTime - 6f) / 12f) * Mathf.PI) * maxAltitude;

        // Азимут: 15°/час. 6ч = восток, 12ч = юг, 18ч = запад (север = +Z, восток = +X)
        float azDeg = bodyTime * 15f + orbitYawOffset;

        float alt = altDeg * Mathf.Deg2Rad;
        float az = azDeg * Mathf.Deg2Rad;
        Vector3 toBody = new Vector3(
            Mathf.Cos(alt) * Mathf.Sin(az),   // восток-запад
            Mathf.Sin(alt),                   // вверх
            Mathf.Cos(alt) * Mathf.Cos(az));  // север-юг

        // Светило светит ОТ себя в сцену
        light.transform.rotation = Quaternion.LookRotation(-toBody, Vector3.up);

        float elevationFactor = Mathf.Clamp01(toBody.y * 5f);

        if (isSun)
        {
            // Краснеет у горизонта
            light.color = elevationFactor < 0.5f
                ? Color.Lerp(sunsetSunColor, daySunColor, elevationFactor * 2f)
                : daySunColor;
        }
        else
        {
            light.color = moonColor;
            light.gameObject.SetActive(elevationFactor > 0.01f);
        }

        light.intensity = baseLux * elevationFactor;
    }

    void UpdateWeatherSystem()
    {
        if (!isRaining)
        {
            weatherTimer += Time.deltaTime;
            if (weatherTimer >= weatherCheckInterval)
            {
                weatherTimer = 0f;
                CheckWeatherProbability();
            }
        }
        else
        {
            rainTimer -= Time.deltaTime;
            if (rainTimer <= 0f) SetRain(false);
        }
    }

    void UpdateVolumeTransition()
    {
        if (weatherVolume == null) return;

        float targetBlend = isRaining ? 1f : 0f;
        currentVolumeBlend = Mathf.MoveTowards(currentVolumeBlend, targetBlend, volumeTransitionSpeed * Time.deltaTime);

        // Плавный переход между профилями через blend
        if (clearProfile != null && rainProfile != null)
        {
            if (currentVolumeBlend < 0.5f)
                weatherVolume.profile = clearProfile;
            else
                weatherVolume.profile = rainProfile;
        }
    }

    void UpdateWindshieldRain()
    {
        if (windshieldMaterial == null) return;
        float targetRainAmount = isRaining ? 1f : 0f;
        currentWindshieldRain = Mathf.MoveTowards(currentWindshieldRain, targetRainAmount, rainFadeSpeed * Time.deltaTime);
        windshieldMaterial.SetFloat("_Rain_Amount", currentWindshieldRain);
    }

    void CheckWeatherProbability()
    {
        if (Random.Range(0f, 100f) <= rainChancePercent) StartRain();
    }

    void StartRain()
    {
        isRaining = true;
        rainTimer = rainDurationMinutes * 60f;
        SetRain(true);
    }

    public void SetRain(bool enableRain)
    {
        isRaining = enableRain;

        if (enableRain)
        {
            if (rainParticles != null && !rainParticles.isPlaying) rainParticles.Play();
            if (rainAudio != null && !rainAudio.isPlaying) rainAudio.Play();
        }
        else
        {
            if (rainParticles != null && rainParticles.isPlaying) rainParticles.Stop();
            if (rainAudio != null && rainAudio.isPlaying) rainAudio.Stop();
        }
    }
}