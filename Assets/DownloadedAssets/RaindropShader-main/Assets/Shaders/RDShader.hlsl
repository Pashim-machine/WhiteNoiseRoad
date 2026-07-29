#ifndef RDShader
#define RDShader

// Heartfelt - by Martijn Steinrucken aka BigWings - 2017
// Email:countfrolic@gmail.com Twitter:@The_ArtOfCode
// License Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License.
// HDRP conversion by Dan Gale
// Physics & Movement Integration Update

#define S(a, b, t) smoothstep(a, b, t)

float3 N13(float p)
{
    // from DAVE HOSKINS
    float3 p3 = frac(p * float3(.1031, .11369, .13787));
    p3 += dot(p3, p3.yzx + 19.19);
    return frac(float3((p3.x + p3.y) * p3.z, (p3.x + p3.z) * p3.y, (p3.y + p3.z) * p3.x));
}

float N(float t)
{
    return frac(sin(t * 12345.564) * 7658.76);
}

float Saw(float b, float t)
{
    return S(0., b, t) * S(1., b, t);
}

float2 DropLayer2(float2 uv, float t, float speed)
{
    float2 UV = uv;
    
    // Переворачиваем направление (теперь при езде вперед капли текут ВВЕРХ)
    float isMovingForward = (speed > 0.1) ? 1.0 : 0.0;
    float dir = (isMovingForward > 0.5) ? 1.0 : -1.0;
    
    uv.y += t * 0.75 * dir;
    float2 a = float2(6., 1.);
    float2 grid = a * 2.;
    float2 id = floor(uv * grid);
    
    float colShift = N(id.x);
    uv.y += colShift;
    
    id = floor(uv * grid);
    float3 n = N13(id.x * 35.2 + id.y * 2376.1);
    float2 st = frac(uv * grid) - float2(.5, 0);
    
    // Переворачиваем шлейф капли для движения вверх
    if (isMovingForward <= 0.5)
    {
        st.y = 1.0 - st.y;
    }
    
    float x = n.x - .5;
    
    float y = UV.y * 20.;
    float wiggle = sin(y + sin(y));
    x += wiggle * (.5 - abs(x)) * (n.z - .5);
    x *= .7;
    float ti = frac(t + n.z);
    y = (Saw(.85, ti) - .5) * .9 + .5;
    float2 p = float2(x, y);
    
    float d = length((st - p) * a.yx);
    
    float mainDrop = S(.4, .0, d);
    
    float r = sqrt(S(1., y, st.y));
    float cd = abs(st.x - x);
    float trail = S(.23*r, .15*r*r, cd);
    float trailFront = S(-.02, .02, st.y - y);
    trail *= trailFront * r * r;
    
    y = UV.y;
    float trail2 = S(.2*r, .0, cd);
    float droplets = max(0., (sin(y * (1. - y) * 120.) - st.y)) * trail2 * trailFront * n.z;
    y = frac(y * 10.) + (st.y - .5);
    float dd = length(st - float2(x, y));
    droplets = S(.3, 0., dd);
    float m = mainDrop + droplets * r * trailFront;
    
    return float2(m, trail);
}

float StaticDrops(float2 uv, float t)
{
    uv *= 40.;
    
    float2 id = floor(uv);
    uv = frac(uv) - .5;
    float3 n = N13(id.x * 107.45 + id.y * 3543.654);
    float2 p = (n.xy - .5) * .7;
    float d = length(uv - p);
    
    float fade = Saw(.025, frac(t + n.z));
    float c = S(.3, 0., d) * frac(n.z * 10.) * fade;
    return c;
}

float2 Drops(float2 uv, float t, float l0, float l1, float l2, float speed)
{
    float s = StaticDrops(uv, t) * l0;
    float2 m1 = DropLayer2(uv, t, speed) * l1;
    float2 m2 = DropLayer2(uv * 1.85, t, speed) * l2;
    
    float c = s + m1.x + m2.x;
    c = S(.3, 1., c);
    
    return float2(c, max(m1.y * l0, m2.y * l1));
}

// Вход переименован на _customTime
void frag4_float(float4 _uvParams, float4 _params, float _speed, float _customTime, out float2 _Out, out float _Lod)
{
    
    float2 uvP = float2(_uvParams.x, _uvParams.y);
    float2 size = float2(_uvParams.z, _uvParams.w);
    float blur = _params.x;
    float trailBlur = _params.y;
    float zoom = _params.z;
    float rainAmount = _params.w;

    // Используем наше переданное время
    float T = _customTime;
    
    float2 uv = (uvP * size) - (size * 0.5);
    
    // ПОВОРАЧИВАЕМ СЕТКУ НА 90 ГРАДУСОВ (исправляем право/лево на верх/низ):
    uv = uv.yx;

    // Увеличиваем скорость ползания капель при разгоне машины
    float speedMult = (_speed > 0.1)
        ? lerp(1.0, 3.5, saturate(_speed / 100.0))
        : lerp(1.0, 2.5, saturate(abs(_speed) / 50.0));
        
    float t = T * .2 * speedMult;
    
    uv *= .7 + zoom * .3;
    
    float staticDrops = S(0., 1., rainAmount) * 2.;
    float layer1 = S(.25, .75, rainAmount);
    float layer2 = S(.0, .5, rainAmount);
    
    float2 c = Drops(uv, t, staticDrops, layer1, layer2, _speed);
    float2 e = float2(.001, 0.);
    float cx = Drops(uv + e, t, staticDrops, layer1, layer2, _speed).x;
    float cy = Drops(uv + e.yx, t, staticDrops, layer1, layer2, _speed).x;
    float2 n = float2(cx - c.x, cy - c.x);

    c.y *= trailBlur - S(80., 100., T) * .8;
    float focus = lerp(blur - c.y, blur, S(.1, .2, c.x));

    float2 dropUV = float2(n.x, n.y);

    _Out = dropUV;
    _Lod = focus;
}

#endif