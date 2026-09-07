using System.Collections.Generic;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// TIA Portal V21 features advertised by Bootstrap.
    /// </summary>
    public enum TiaFeature
    {
        /// <summary>Hardware-level HMI connections via Siemens.Engineering.HW.CommunicationConnections.</summary>
        HardwareHmiConnection,

        /// <summary>SIMATIC SD / S7DCL document export via ExportAsDocuments.</summary>
        DocumentExport
    }

    public class CapabilityInfo
    {
        public string Feature { get; set; } = "";
        public bool Supported { get; set; }
        public int MinVersion { get; set; }
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// Describes the V21 API capabilities available to this server.
    /// </summary>
    internal static class Capability
    {
        /// <summary>Returns an independent capability snapshot for each Bootstrap response.</summary>
        public static List<CapabilityInfo> Snapshot()
        {
            return new List<CapabilityInfo>
            {
                new CapabilityInfo
                {
                    Feature = nameof(TiaFeature.HardwareHmiConnection),
                    Supported = true,
                    MinVersion = Engineering.TiaMajorVersion,
                    Note = "TIA Portal V21 provides hardware-level HMI connections through Siemens.Engineering.HW.CommunicationConnections."
                },
                new CapabilityInfo
                {
                    Feature = nameof(TiaFeature.DocumentExport),
                    Supported = true,
                    MinVersion = Engineering.TiaMajorVersion,
                    Note = "TIA Portal V21 provides SIMATIC SD / S7DCL document export through ExportAsDocuments."
                }
            };
        }
    }
}
