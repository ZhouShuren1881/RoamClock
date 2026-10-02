namespace RoamClock.WeatherFetch;

/// <summary>Open-Meteo WMO codes, shared by the panel and its tooltip.</summary>
public static class WeatherConditions
{
    public static string Describe(int code) => code switch
    {
        0 => "晴",
        1 => "大部晴朗",
        2 => "局部多云",
        3 => "阴",
        45 or 48 => "雾",
        51 or 53 or 55 => "毛毛雨",
        56 or 57 => "冻毛毛雨",
        61 or 63 or 65 => "雨",
        66 or 67 => "冻雨",
        71 or 73 or 75 or 77 => "雪",
        80 or 81 or 82 => "阵雨",
        85 or 86 => "阵雪",
        95 or 96 or 99 => "雷暴",
        _ => "未知天气"
    };

    // 24 × 24 vector paths; rendered by the host without a weather icon font.
    public static string IconPath(int code, bool isDay) => code switch
    {
        0 or 1 => isDay
            ? "M12,7 A5,5 0 1 1 12,17 A5,5 0 1 1 12,7 M12,1 L12,3 M12,21 L12,23 M1,12 L3,12 M21,12 L23,12 M4,4 L6,6 M18,18 L20,20 M4,20 L6,18 M18,6 L20,4"
            : "M19,16 A9,9 0 1 1 8,3 A8,8 0 0 0 19,16 Z",
        2 => "M8,4 A4,4 0 1 1 4,8 M8,1 L8,2 M1,8 L2,8 M2,2 L3,3 M6,20 C0,20 0,12 6,12 C7,6 16,6 17,12 C24,11 25,20 19,20 Z",
        3 => Cloud,
        45 or 48 => "M3,5 L21,5 M1,10 L18,10 M5,15 L23,15 M2,20 L20,20",
        >= 51 and <= 67 or >= 80 and <= 82 => Cloud + " M7,19 L5,23 M13,19 L11,23 M19,19 L17,23",
        >= 71 and <= 77 or 85 or 86 => "M12,1 L12,23 M2,6 L22,18 M2,18 L22,6 M9,3 L12,6 L15,3 M9,21 L12,18 L15,21 M2,10 L6,9 L5,5 M19,19 L18,15 L22,14 M2,14 L6,15 L5,19 M19,5 L18,9 L22,10",
        95 or 96 or 99 => Cloud + " M13,15 L9,20 L13,20 L10,24",
        _ => "M12,2 A10,10 0 1 1 12,22 A10,10 0 1 1 12,2 M8,8 C8,3 18,4 16,10 C15,12 12,11 12,15 M12,18 L12,19"
    };

    private const string Cloud = "M6,16 C0,16 0,8 6,8 C7,1 17,1 18,8 C24,8 25,16 19,16 Z";
}
