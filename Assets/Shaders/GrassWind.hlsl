#ifndef GRASS_WIND_INCLUDED
#define GRASS_WIND_INCLUDED

void GrassWind_float(
    float3 PositionOS,
    float3 PositionWS,
    float WindStrength,
    float WindSpeed,
    float WindScale,
    float TimeValue,
    out float3 OffsetOS
)
{
    // Пространственная фаза ветра.
    // World Space делает ветер разным в разных местах мира.
    float phaseX =
        PositionWS.x * WindScale +
        TimeValue * WindSpeed;

    float phaseZ =
        PositionWS.z * WindScale * 0.7 +
        TimeValue * WindSpeed * 0.7;

    // Две дешёвые волны.
    float wave = sin(phaseX);
    float wave2 = cos(phaseZ);

    // Смешиваем.
    float wind = wave + wave2 * 0.5;

    // Сила ветра.
    wind *= WindStrength;

    // Смещение в Object Space.
    OffsetOS = float3(
        wind,
        0.0,
        wind * 0.35
    );
}

#endif