#ifndef RAIN_DROPS_INCLUDED
#define RAIN_DROPS_INCLUDED

float2 hash22(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float DropsHeight(float2 uv, float t, float amount, float flow, float density)
{
    float h = 0.0;
    for (int i = 0; i < 2; i++)
    {
        float scale = lerp(9.0, 26.0, (float) i);
        float2 p = uv * scale;

        // Дифференциальный поток: каждая колонна течёт со своей скоростью,
        // направление и сила задаются flow (+ = вниз, - = вверх/назад)
        float col = floor(p.x);
        float colRnd = hash22(float2(col, i * 13.7)).x;
        p.y += t * flow * (0.35 + 0.9 * colRnd);

        float2 id = floor(p);
        float2 f = frac(p);

        float2 rnd = hash22(id);
        // density делает капли РЕЖЕ: включается только доля amount*density ячеек
        float on = step(1.0 - amount * density, rnd.x);

        float2 c = 0.5 + 0.3 * sin(t * (0.4 + rnd.y) + rnd * 6.2831);
        float2 q = f - c;
        q.y /= (1.0 + abs(flow) * 1.2); // на скорости капли вытягиваются вдоль потока
        float d = length(q);
        float r = 0.22 + 0.18 * rnd.y;
        h += on * smoothstep(r, r * 0.25, d);
    }
    return h;
}

void RainDrop_float(float2 uv, float t, float amount, float flow, float density,
                    out float height, out float2 grad)
{
    float e = 0.02;
    float h = DropsHeight(uv, t, amount, flow, density);
    float hx = DropsHeight(uv + float2(e, 0.0), t, amount, flow, density);
    float hy = DropsHeight(uv + float2(0.0, e), t, amount, flow, density);
    height = h;
    grad = float2(hx - h, hy - h) / e;
}

#endif