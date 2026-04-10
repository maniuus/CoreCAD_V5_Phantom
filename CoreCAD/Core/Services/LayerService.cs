using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using System;

namespace CoreCAD.Core.Services
{
    /// <summary>
    /// Manages architectural and structural layer standards for the CoreCAD system.
    /// Values are retrieved from 'drawing_standard.json' via JsonService.
    /// </summary>
    public static class LayerService
    {
        // Standard Layer Fetchers
        public static string WallLayer => JsonService.GetLayerConfig("Wall").Name;
        public static short ColorWall => JsonService.GetLayerConfig("Wall").Color;

        public static string WallHatchLayer => JsonService.GetLayerConfig("WallHatch").Name;
        public static short ColorWallHatch => JsonService.GetLayerConfig("WallHatch").Color;

        public static string DoorLayer => JsonService.GetLayerConfig("Door").Name;
        public static short ColorDoor => JsonService.GetLayerConfig("Door").Color;

        public static string ColumnLayer => JsonService.GetLayerConfig("Column").Name;
        public static short ColorColumn => JsonService.GetLayerConfig("Column").Color;

        public static string AnnotationLayer => JsonService.GetLayerConfig("Annotation").Name;
        public static short ColorAnno => JsonService.GetLayerConfig("Annotation").Color;

        public static string CenterlineLayer => JsonService.GetLayerConfig("Centerline").Name;
        public static short ColorCenterline => JsonService.GetLayerConfig("Centerline").Color;

        public static string BackboneLayer => JsonService.GetLayerConfig("Backbone").Name;
        public static short ColorBackbone => JsonService.GetLayerConfig("Backbone").Color;

        /// <summary>
        /// Ensures a specific layer exists in the database with the requested properties.
        /// </summary>
        public static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex, string linetype = "Continuous")
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord
                {
                    Name = layerName,
                    Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
                };

                // Check for linetype existence
                LinetypeTable ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has(linetype))
                {
                    ltr.LinetypeObjectId = ltt[linetype];
                }

                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }
    }
}
