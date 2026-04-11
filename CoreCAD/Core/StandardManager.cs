using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using CoreCAD.Models;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;

namespace CoreCAD.Core
{
    public class StandardManager
    {
        private static StandardManager? _instance;
        public static StandardManager Instance => _instance ??= new StandardManager();

        public StandardConfig Config { get; private set; } = new StandardConfig();

        public StandardManager()
        {
            Load();
        }

        public void Load()
        {
            try
            {
                // [ROBUST] Cek beberapa lokasi untuk mencari drawing_standard.json
                string[] locations = new string[] {
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
                    "d:\\ABIMANYU\\Devlab\\CoreCAD_V2\\CoreCAD", // Dev fallback
                    "."
                };

                foreach (string? loc in locations)
                {
                    if (string.IsNullOrEmpty(loc)) continue;
                    string path = Path.Combine(loc, "drawing_standard.json");
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        var deserialized = JsonConvert.DeserializeObject<StandardConfig>(json);
                        if (deserialized != null) {
                            Config = deserialized;
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        public string GetLayer(string key)
        {
            if (Config.Layers.TryGetValue(key, out var layer))
                return layer.Name;
            return "0";
        }

        /// <summary>
        /// Pastikan layer ada di database. Jika belum, buat dengan warna yang sesuai standard.
        /// </summary>
        public void EnsureLayer(Database db, Transaction tr, string key)
        {
            if (!Config.Layers.TryGetValue(key, out var cfg)) return;

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            LayerTableRecord? ltr = null;

            if (!lt.Has(cfg.Name))
            {
                lt.UpgradeOpen();
                ltr = new LayerTableRecord { Name = cfg.Name };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
            else
            {
                ltr = (LayerTableRecord)tr.GetObject(lt[cfg.Name], OpenMode.ForWrite);
            }

            // [STRICT] Paksa warna sesuai standard (Force Color)
            if (ltr != null)
            {
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, cfg.Color);
            }
        }
    }
}
