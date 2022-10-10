namespace SawayaSharp.Utils;

public class Util
{
    public static string FormatTimeSpan(TimeSpan timeSpan) {
        if (timeSpan.TotalDays >= 1000000) return "Stream";
        
        var dayFormat = timeSpan.TotalDays >= 1 ? "d\\." : "";
        var hourFormat = timeSpan.TotalHours >= 1 ? "hh\\:" : "";

        return timeSpan.ToString($"{dayFormat}{hourFormat}mm\\:ss");
    }
}