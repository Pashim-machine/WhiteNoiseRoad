using UnityEngine;
using UnityEngine.Rendering; // Обязательно для работы с Volume

public class HDRPWeatherManager : MonoBehaviour
{
    [Header("Настройки времени суток")]
    [Tooltip("Сколько реальных секунд длится один полный игровой день (24 часа)")]
    public float dayLengthInRealSeconds = 300f; // По умолчанию 5 минут на полный цикл, можешь поставить хоть 86400 для реального времени

    [Range(0f, 24f)]
    [Tooltip("Текущее время суток (в часах). Можно менять прямо в инспекторе!")]
    public float currentTimeOfDay = 12f; // Начинаем с полудня

    [Header("Настройки погоды")]
    [Range(0f, 100f)]
    [Tooltip("Вероятность выпадения дождя (в процентах) при проверке")]
    public float rainChancePercent = 10f; // 10%

    [Tooltip("Как часто проверять смену погоды (в реальных секундах)")]
    public float weatherCheckInterval = 30f;

    [Tooltip("Сколько минут идет дождь")]
    public float rainDurationMinutes = 20f; // 20 минут

    [Header("Ссылки на объекты")]
    public Light sunLight;                  // Солнце (Directional Light)
    public ParticleSystem rainParticles;    // Частицы дождя
    public AudioSource rainAudio;           // Звук дождя
    public Volume weatherVolume;            // Volume со сцены
    public VolumeProfile clearProfile;      // Профиль ясной погоды
    public VolumeProfile rainProfile;       // Профиль дождливой погоды

    [Header("Интенсивность солнца (в Люксах)")]
    public float clearSunLux = 100000f;     // Яркое солнце днем
    public float rainSunLux = 15000f;       // Приглушенное солнце при тучах

    // Внутренние таймеры
    private bool isRaining = false;
    private float weatherTimer = 0f;
    private float rainTimer = 0f;

    void Start()
    {
        // Инициализация при старте
        SetRain(false);
    }

    void Update()
    {
        UpdateTimeOfDay();
        UpdateWeatherSystem();
    }

    void UpdateTimeOfDay()
    {
        // Вычисляем, сколько часов проходит за одну секунду
        float hoursPerSecond = 24f / dayLengthInRealSeconds;
        currentTimeOfDay += hoursPerSecond * Time.deltaTime;

        // Если прошли сутки, сбрасываем на 0
        if (currentTimeOfDay >= 24f)
        {
            currentTimeOfDay = 0f;
        }

        // Плавное вращение солнца на основе текущего часа (от 0 до 24)
        // 0 часов (ночь) = -90°, 6 часов (утро) = 0°, 12 часов (день) = 90°, 18 часов (вечер) = 180°
        float sunAngle = (currentTimeOfDay / 24f) * 360f - 90f;
        if (sunLight != null)
        {
            sunLight.transform.rotation = Quaternion.Euler(sunAngle, 170f, 0f);
        }
    }

    void UpdateWeatherSystem()
    {
        if (!isRaining)
        {
            // Считаем время до следующей проверки погоды
            weatherTimer += Time.deltaTime;
            if (weatherTimer >= weatherCheckInterval)
            {
                weatherTimer = 0f;
                CheckWeatherProbability();
            }
        }
        else
        {
            // Если идет дождь, отсчитываем время до его окончания
            rainTimer -= Time.deltaTime;
            if (rainTimer <= 0f)
            {
                SetRain(false);
            }
        }
    }

    void CheckWeatherProbability()
    {
        // Проверяем 10% вероятность
        float roll = Random.Range(0f, 100f);
        if (roll <= rainChancePercent)
        {
            StartRain();
        }
    }

    void StartRain()
    {
        isRaining = true;
        // Переводим минуты в секунды для таймера
        rainTimer = rainDurationMinutes * 60f;
        SetRain(true);
        Debug.Log("Пошел дождь! Продлится " + rainDurationMinutes + " минут.");
    }

    public void SetRain(bool enableRain)
    {
        isRaining = enableRain;

        if (enableRain)
        {
            if (rainParticles != null && !rainParticles.isPlaying) rainParticles.Play();
            if (rainAudio != null && !rainAudio.isPlaying) rainAudio.Play();
            if (sunLight != null) sunLight.intensity = rainSunLux;
            if (weatherVolume != null && rainProfile != null) weatherVolume.profile = rainProfile;
        }
        else
        {
            if (rainParticles != null && rainParticles.isPlaying) rainParticles.Stop();
            if (rainAudio != null && rainAudio.isPlaying) rainAudio.Stop();
            if (sunLight != null) sunLight.intensity = clearSunLux;
            if (weatherVolume != null && clearProfile != null) weatherVolume.profile = clearProfile;
        }
    }
}