using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Models;
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

                    Line wallLine = new Line(ppr1.Value, ppr2.Value);
                    wallLine.SetDatabaseDefaults();

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
                    ed.WriteMessage($"\n--- KTP DINDING DITEMUKAN ---");
                    ed.WriteMessage($"\nGUID  : {data.Id}");
                    ed.WriteMessage($"\nTebal : {data.Thickness} mm");
                    ed.WriteMessage($"\n-----------------------------");
                }
                else
                {
                    ed.WriteMessage("\n[CoreCAD] Ini garis biasa, tidak punya KTP CoreCAD.");
                }

                tr.Commit();
            }
        }
    }
}
