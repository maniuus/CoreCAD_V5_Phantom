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
            // Register for document opening to ensure each new drawing gets the AppID
            AcadApp.DocumentManager.DocumentCreated += (s, e) =>
            {
                RegisterCoreApp(e.Document);
            };

            // Register for the current document (if any)
            if (AcadApp.DocumentManager.MdiActiveDocument != null)
            {
                RegisterCoreApp(AcadApp.DocumentManager.MdiActiveDocument);
            }
        }

        public void Terminate()
        {
            // Cleanup if necessary
        }

        private void RegisterCoreApp(Document doc)
        {
            if (doc == null) return;

            Autodesk.AutoCAD.DatabaseServices.Database db = doc.Database;

            // Register Event Handler to monitor object cloning/copying
            db.ObjectAppended -= XDataManager.OnObjectAppended;
            db.ObjectAppended += XDataManager.OnObjectAppended;

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    XDataManager.RegisterApp(tr, db);
                    tr.Commit();
                }
            }
        }
    }
}
