namespace TiaBridge.Worker.V19;

// A native Device may contain both kinds of software. Traversal order must not
// overwrite PLC with HMI or declare an unknown/mixed parent fully classified.
internal static class InventoryFamily
{
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
