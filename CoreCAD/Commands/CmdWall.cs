using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Models;
using CoreCAD.Core;
using CoreCAD.Persistence;  // namespace actual untuk Database/XDataManager.cs

namespace CoreCAD.Commands
{
    /// <summary>
    /// [V5 COMMANDS] Perintah user untuk operasi dinding berbasis data (KTP).
    ///   CC_WALL      — Gambar garis As dan suntikkan KTP (WallData)
    ///   CC_CEK_KTP   — Scanner: baca dan tampilkan KTP dari entitas yang dipilih
    /// </summary>
    public class CmdWall
    {
        [CommandMethod("CC_WALL")]
        public void DrawWallLine()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            Editor ed = doc.Editor;

            PromptPointResult ppr1 = ed.GetPoint("\nKlik titik awal dinding (As): ");
            if (ppr1.Status != PromptStatus.OK) return;

            PromptPointOptions ppo2 = new PromptPointOptions("\nKlik titik akhir dinding (As): ")
            {
                UseBasePoint = true,
                BasePoint = ppr1.Value,
                UseDashedLine = true
            };
            PromptPointResult ppr2 = ed.GetPoint(ppo2);
            if (ppr2.Status != PromptStatus.OK) return;

            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // [STANDARD] Pastikan Layer Centerline ada sebelum menggambar
                    StandardManager.Instance.Load();
                    StandardManager.Instance.EnsureLayer(db, tr, "Centerline");

                    Line wallLine = new Line(ppr1.Value, ppr2.Value);
                    wallLine.SetDatabaseDefaults();
                    
                    // Set Layer ke C-CENT (Color 144) otomatis
                    wallLine.Layer = StandardManager.Instance.GetLayer("Centerline");
                    wallLine.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);

                    btr.AppendEntity(wallLine);
                    tr.AddNewlyCreatedDBObject(wallLine, true);

                    WallData newData = new WallData(150.0);
                    XDataManager.AttachWallData(tr, db, wallLine, newData);

                    tr.Commit();
                    ed.WriteMessage($"\n[CoreCAD] Sukses! Garis As dinding terdaftar dengan ID: {newData.Id}");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[Error CC_WALL]: {ex.Message}");
                    tr.Abort();
                }
            }
        }

        [CommandMethod("CC_CEK_KTP")]
        public void CheckWallData()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            PromptEntityOptions peo = new PromptEntityOptions("\nPilih garis untuk dicek KTP-nya: ");
            peo.SetRejectMessage("\nHanya bisa pilih Garis (Line)!");
            peo.AddAllowedClass(typeof(Line), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                WallData? data = XDataManager.GetWallData(ent);

                if (data != null)
                {
                    bool isModern = ent.GetXDataForApplication(XDataManager.APP_NAME)?.AsArray()
                                        .Any(tv => tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString && tv.Value?.ToString()?.StartsWith("{") == true) ?? false;

                    ed.WriteMessage($"\n--- KTP DINDING DITEMUKAN ---");
                    ed.WriteMessage($"\nSOURCE: {(isModern ? "MODERN (JSON)" : "LEGACY (FLAT)")}");
                    ed.WriteMessage($"\nGUID  : {data.Id}");
                    ed.WriteMessage($"\nTebal : {data.Thickness} mm");
                    
                    if (ent is Line line)
                        ed.WriteMessage($"\nPanjang: {line.Length:F2} mm");

                    ed.WriteMessage($"\nLubang: {data.Openings.Count} buah");
                    foreach (var op in data.Openings)
                    {
                        ed.WriteMessage($"\n  > {op.BlockName}: Pos {op.Position}mm, Lebar {op.Width}mm");
                    }

                    // [V2 DIAGNOSTICS] Show Raw JSON
                    string raw = XDataManager.GetRawJSON(ent);
                    ed.WriteMessage($"\nRAW JSON: {(string.IsNullOrEmpty(raw) ? "NONE" : raw)}");
                    ed.WriteMessage($"\n-----------------------------");
                }
                else
                {
                    ed.WriteMessage("\n[CoreCAD] Ini garis biasa, tidak punya KTP CoreCAD.");
                }

                tr.Commit();
            }
        }

        [CommandMethod("CC_ADD_OPENING")]
        public void AddOpening()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptEntityOptions peo = new PromptEntityOptions("\nPilih dinding untuk dipasang lubang/pintu: ");
            peo.SetRejectMessage("\nHanya bisa pilih Garis (Line)!");
            peo.AddAllowedClass(typeof(Line), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForWrite);
                WallData? data = XDataManager.GetWallData(ent);

                if (data == null)
                {
                    ed.WriteMessage("\n[Error] Entitas ini tidak memiliki data dinding CoreCAD.");
                    return;
                }

                // Input Parameter
                PromptDoubleOptions pdoPos = new PromptDoubleOptions("\nJarak lubang dari Start (mm): ");
                PromptDoubleResult pdrPos = ed.GetDouble(pdoPos);
                if (pdrPos.Status != PromptStatus.OK) return;

                PromptDoubleOptions pdoWidth = new PromptDoubleOptions("\nLebar lubang (mm): ") { DefaultValue = 900.0 };
                PromptDoubleResult pdrWidth = ed.GetDouble(pdoWidth);
                if (pdrWidth.Status != PromptStatus.OK) return;

                PromptStringOptions psoBlock = new PromptStringOptions("\nNama Block Simbol (misal: PINTU_PJ1): ") { AllowSpaces = false, DefaultValue = "PINTU_PJ1" };
                PromptResult prBlock = ed.GetString(psoBlock);
                if (prBlock.Status != PromptStatus.OK) return;

                // Create and Add Opening
                Opening newOp = new Opening(pdrPos.Value, pdrWidth.Value, prBlock.StringResult);
                data.Openings.Add(newOp);

                // Re-attach data (Serialize JSON)
                XDataManager.AttachWallData(tr, db, ent, data);

                tr.Commit();
                ed.WriteMessage($"\n[CoreCAD] Lubang berhasil ditambahkan.");
                ed.WriteMessage($"\n[CoreCAD] Memory Count: {data.Openings.Count} lubang.");
                ed.WriteMessage("\n[CoreCAD] Jalankan CC_REBUILD untuk melihat hasil.");
            }
        }
    }
}
