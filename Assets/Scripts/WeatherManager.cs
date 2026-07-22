using UnityEngine;

public class WeatherManager : MonoBehaviour
{
    public ParticleSystem rainParticles;
    public AudioSource rainAudio;
    public Light sunLight; // Ссылка на твое солнце (Directional Light)

    public void SetRain(bool isRaining)
    {
        if (isRaining)
        {
            rainParticles.Play();
            rainAudio.Play();
            sunLight.intensity = 0.2f; // Приглушаем свет
            RenderSettings.fog = true; // Включаем туман
        }
        else
        {
            rainParticles.Stop();
            rainAudio.Stop();
            sunLight.intensity = 1.0f; // Возвращаем свет
            RenderSettings.fog = false;
        }
    }
}
