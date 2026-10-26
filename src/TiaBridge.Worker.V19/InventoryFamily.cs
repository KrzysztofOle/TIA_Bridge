namespace TiaBridge.Worker.V19;

// A native Device may contain both kinds of software. Traversal order must not
// overwrite PLC with HMI or declare an unknown/mixed parent fully classified.
internal static class InventoryFamily
{
    internal static string HmiKind(string? softwareType) => softwareType switch
    {
        "Siemens.Engineering.HmiUnified.HmiSoftware" => "UNIFIED",
        "Siemens.Engineering.Hmi.HmiSoftware" => "CLASSIC",
        _ => "UNKNOWN"
    };

    internal static string ClassifyDetailed(IEnumerable<string?> softwareTypes)
    {
        bool plc = false, unified = false, classic = false;
        foreach (string? type in softwareTypes)
        {
            plc |= type == "Siemens.Engineering.SW.PlcSoftware";
            unified |= type == "Siemens.Engineering.HmiUnified.HmiSoftware";
            classic |= type == "Siemens.Engineering.Hmi.HmiSoftware";
        }
        int families = (plc ? 1 : 0) + (unified ? 1 : 0) + (classic ? 1 : 0);
        if (families != 1) return "UNKNOWN";
        return plc ? "PLC" : unified ? "HMI_UNIFIED" : "HMI_CLASSIC";
    }

    internal static string Classify(IEnumerable<string> softwareTypes)
    {
        bool plc = false, hmi = false;
        foreach (string type in softwareTypes)
        {
            plc |= type == "Siemens.Engineering.SW.PlcSoftware";
            hmi |= type == "Siemens.Engineering.Hmi.HmiSoftware" || type == "Siemens.Engineering.HmiUnified.HmiSoftware";
        }
        return plc == hmi ? "UNKNOWN" : plc ? "PLC" : "HMI";
    }
}
