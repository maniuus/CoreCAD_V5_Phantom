using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CoreCAD.Persistence;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CoreCAD.App
{
    public class Initialization : IExtensionApplication
    {
        public void Initialize()
        {
            // 1. Jalankan Penjaga Database (Event Watcher)
            DatabaseWatcher.Initialize();

            // 2. Registrasi AppID CoreCAD di setiap dokumen baru
            AcadApp.DocumentManager.DocumentCreated += (s, e) => RegisterCoreApp(e.Document);

            if (AcadApp.DocumentManager.MdiActiveDocument != null)
                RegisterCoreApp(AcadApp.DocumentManager.MdiActiveDocument);
        }

        public void Terminate() { }

        private void RegisterCoreApp(Document doc)
        {
            if (doc == null) return;
            Database db = doc.Database;

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Pastikan RegApp tersedia (Old logic backup)
                    var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                    if (!rat.Has(XDataHelper.RegAppName))
                    {
                        rat.UpgradeOpen();
                        var ratr = new RegAppTableRecord { Name = XDataHelper.RegAppName };
                        rat.Add(ratr);
                        tr.AddNewlyCreatedDBObject(ratr, true);
                    }
                    tr.Commit();
                }
            }
        }
    }
}
