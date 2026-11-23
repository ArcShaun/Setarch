namespace SetArch.Core.Calculators;

/// <summary>
/// Suggested calculator inputs for an IBC/FBC occupancy group, so choosing "B" on the project form
/// pre-selects "Business areas", "BUSINESS (B)" and "BUSINESS - New". These are only starting points;
/// every choice can be changed.
/// </summary>
public static class CodeDefaults
{
    /// <summary>Table 1004.5 function of space typical for the group.</summary>
    public static string? FunctionOfSpace(string group) => Group(group) switch
    {
        "A-1" => "Assembly with fixed seats",
        "A-2" => "Assembly without fixed seats - Unconcentrated (tables and chairs)",
        "A-3" => "Assembly without fixed seats - Concentrated (chairs only, not fixed)",
        "A-4" => "Assembly with fixed seats - Coliseum / arena / indoor sporting events",
        "A-5" => "Assembly with fixed seats - Stadium / bleachers / grandstand",
        "B" => "Business areas",
        "E" => "Educational - Classroom area",
        "F-1" or "F-2" => "Industrial areas",
        "H-1" or "H-2" or "H-3" or "H-4" or "H-5" => "Group H-5 Fabrication and manufacturing areas",
        "I-1" => "Institutional - Assisted living: care recipient sleeping units",
        "I-2" => "Institutional - Hospital: care recipient sleeping units",
        "I-3" => "Institutional - Prison cells",
        "I-4" => "Day care",
        "M" => "Mercantile",
        "R-1" => "Residential - Hotel, motel or boarding house guestrooms",
        "R-2" => "Residential",
        "R-3" => "Residential - One- and two-family dwelling or lodging house",
        "R-4" => "Residential - Congregate living, 16 or fewer care recipients",
        "S-1" or "S-2" => "Warehouses",
        _ => null,
    };

    /// <summary>FBC occupancy on the egress calculator.</summary>
    public static string? FbcEgress(string group)
    {
        var g = Group(group);
        if (g.StartsWith("A-")) return "ASSEMBLY (A-1 thru A-5)";
        return g switch
        {
            "B" => "BUSINESS (B)",
            "E" => "EDUCATIONAL (E)",
            "M" => "MERCANTILE (M)",
            "F-1" => "FACTORY / INDUSTRIAL F-1",
            "F-2" => "FACTORY / INDUSTRIAL F-2",
            "I-1" or "I-2" or "I-3" or "I-4" => "INSTITUTIONAL " + g,
            "R-1" or "R-2" or "R-3" or "R-4" => "RESIDENTIAL " + g,
            "S-1" => "STORAGE S-1",
            "S-2" => "STORAGE S-2",
            _ => null,
        };
    }

    /// <summary>FFPC / NFPA 101 occupancy on the egress calculator.</summary>
    public static string? FfpcEgress(string group, bool existingBuilding)
    {
        var age = existingBuilding ? "Existing" : "New";
        var g = Group(group);
        if (g.StartsWith("A-")) return "ASSEMBLY - " + age;
        return g switch
        {
            "B" => "BUSINESS - " + age,
            "E" => "EDUCATIONAL - " + age,
            "M" => "MERCANTILE Class A/B/C - " + age,
            "F-1" or "F-2" => "INDUSTRIAL - General",
            "I-2" => "HEALTH CARE",
            "I-3" => "DETENTION AND CORRECTIONAL",
            "I-4" => "DAY CARE - " + age,
            "R-1" => "RESIDENTIAL Hotels/Dormitories - " + age,
            "R-2" => "RESIDENTIAL Apartment Bldgs - " + age,
            "S-1" => "STORAGE - Ordinary Hazard",
            "S-2" => "STORAGE - Low Hazard",
            _ => null,
        };
    }

    /// <summary>"R-3 Residential" / "r3" → "R-3".</summary>
    public static string Group(string? occupancy)
    {
        var s = (occupancy ?? "").Trim().ToUpperInvariant();
        var token = s.Split(' ', ',', '/', '(')[0];
        if (token.Length == 2 && char.IsLetter(token[0]) && char.IsDigit(token[1])) token = $"{token[0]}-{token[1]}";
        return token;
    }
}
