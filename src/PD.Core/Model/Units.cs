namespace PD.Core.Model;

/// <summary>Internal unit is feet; convert at the boundary.</summary>
public static class Units
{
    public static double ToFeet(string unitSystem) => unitSystem switch
    {
        "Millimeters" => 1.0 / 304.8,
        "Centimeters" => 1.0 / 30.48,
        "Meters" => 1.0 / 0.3048,
        "Inches" => 1.0 / 12.0,
        "Feet" => 1.0,
        _ => throw new ArgumentException($"Unsupported unit system '{unitSystem}'", nameof(unitSystem)),
    };
}
