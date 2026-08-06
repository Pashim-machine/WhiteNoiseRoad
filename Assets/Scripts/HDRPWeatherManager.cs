using UnityEngine;
using UnityEngine.Rendering;

public class HDRPWeatherManager : MonoBehaviour
{
    [Header("Настройки времени суток")]
    public float dayLengthInRealSeconds = 300f;
    [Range(0f, 24f)]
    public float currentTimeOfDay = 8f; // Начинаем утром

    [Header("Светила (Направленные источники)")]
    public Light sunLight;                  // Солнце
    public Light moonLight;                 // Луна (Directional Light)
    public float sunBaseLux = 100000f;
    public float moonBaseLux = 10000f;      // Увеличили до 10к для красивых ночных теней
    public float rainSunLux = 15000f;

    [Header("Цвета неба и света")]
    public Color daySunColor = new Color(1f, 0.95f, 0.8f);
    public Color sunsetSunColor = new Color(1f, 0.5f, 0.2f);
    public Color moonColor = new Color(0.6f, 0.7f, 1f);

    [Header("Настройки погоды")]
    [Range(0f, 100f)]
    public float rainChancePercent = 10f;
    public float weatherCheckInterval = 30f;
    public float rainDurationMinutes = 20f;

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

    void Start()
    {
        SetRain(false);
        if (windshieldMaterial != null)
            windshieldMaterial.SetFloat("_Rain_Amount", 0f);

        if (moonLight != null) moonLight.gameObject.SetActive(false);
    }

    void Update()
    {
        UpdateTimeOfDay();
        UpdateWeatherSystem();
        UpdateWindshieldRain();
    }

    void UpdateTimeOfDay()
    {
        float hoursPerSecond = 24f / dayLengthInRealSeconds;
        currentTimeOfDay += hoursPerSecond * Time.deltaTime;
        if (currentTimeOfDay >= 24f) currentTimeOfDay = 0f;

        float sunAngle = (currentTimeOfDay / 24f) * 360f - 90f;

        if (sunLight != null)
        {
            sunLight.transform.rotation = Quaternion.Euler(sunAngle, 170f, 0f);
            float sunElevation = Mathf.Sin(sunAngle * Mathf.Deg2Rad);
            float sunIntensityFactor = Mathf.Clamp01(sunElevation * 5f);

            // Цвета заката
            if (sunIntensityFactor < 0.5f)
                sunLight.color = Color.Lerp(sunsetSunColor, daySunColor, sunIntensityFactor * 2f);
            else
                sunLight.color = daySunColor;

            float targetSunLux = isRaining ? rainSunLux : sunBaseLux;
            sunLight.intensity = targetSunLux * sunIntensityFactor;

            // ДИНАМИЧЕСКИЕ ТЕНИ: Если солнце светит — тени включены. Если село — выключены.
            sunLight.shadows = (sunIntensityFactor > 0.05f) ? LightShadows.Soft : LightShadows.None;
        }

        if (moonLight != null)
        {
            float moonAngle = sunAngle + 180f;
            moonLight.transform.rotation = Quaternion.Euler(moonAngle, 170f, 0f);

            float moonElevation = Mathf.Sin(moonAngle * Mathf.Deg2Rad);
            float moonIntensityFactor = Mathf.Clamp01(moonElevation * 5f);

            moonLight.gameObject.SetActive(moonIntensityFactor > 0.01f);
            moonLight.color = moonColor;
            moonLight.intensity = moonBaseLux * moonIntensityFactor;

            // ДИНАМИЧЕСКИЕ ТЕНИ: Луна забирает тени на себя, когда солнце село.
            moonLight.shadows = (moonIntensityFactor > 0.05f) ? LightShadows.Soft : LightShadows.None;
        }
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
            if (weatherVolume != null && rainProfile != null) weatherVolume.profile = rainProfile;
        }
        else
        {
            if (rainParticles != null && rainParticles.isPlaying) rainParticles.Stop();
            if (rainAudio != null && rainAudio.isPlaying) rainAudio.Stop();
            if (weatherVolume != null && clearProfile != null) weatherVolume.profile = clearProfile;
        }
    }
}