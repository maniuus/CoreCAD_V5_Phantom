using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CoreCAD.Persistence;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CoreCAD.App
{
    public class IdentityManager
    {
        [CommandMethod("CID")] // Alias untuk Check Identity
        public void QuickCheckId()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            
            PromptEntityOptions peo = new PromptEntityOptions("\n[CORE-ID] Pilih objek untuk verifikasi identitas: ");
            peo.AllowNone = false;
            
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                var data = XDataHelper.GetFullCache(ent);

                if (string.IsNullOrEmpty(data.guid))
                {
                    ed.WriteMessage("\n>>> [NULL] Objek tidak memiliki identitas CoreCAD.");
                }
                else
                {
                    ed.WriteMessage("\n========================================");
                    ed.WriteMessage($"\nID      : {data.guid}");
                    ed.WriteMessage($"\nROLE    : {data.role}");
                    ed.WriteMessage($"\nMARK    : {data.mark}");
                    ed.WriteMessage($"\nPARENT  : {data.parent}");
                    ed.WriteMessage("\n========================================");
                }
                tr.Commit();
            }
        }
    }
}
