using System;

namespace CoreCAD.Persistence
{
    /// <summary>
    /// [V5 DATABASE LAYER] Manages all persistence to/from AutoCAD XData and ExtensionDictionary.
    /// Replaces CoreCAD.Core.Registry.XDataManager with a cleaner, V5-compliant API.
    /// 
    /// Responsibilities:
    ///   - Read/Write WallData identity to entity XData (CORECAD_ENGINE)
    ///   - Read/Write GroupId to entity XData (CORECAD_GROUP)
    ///   - Manage parent-child links via ExtensionDictionary
    ///   - Provide the Silent Guard (SafeUpgradeOpen)
    /// </summary>
    public static class XDataManager
    {
        // TODO V5: Refactor dari CoreCAD.Core.Registry.XDataManager ke sini.
        // Gunakan WallData sebagai DTO antar layer Engine <-> Database.

        public const string RegAppName = "CORECAD_ENGINE";
        public const string GroupRegAppName = "CORECAD_GROUP";
    }
}
