using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Core.Geometry;
using CoreCAD.Core.Services;
using System;

namespace CoreCAD.Core
{
    public class Initializer : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                // 1. Subscribe to FUTURE document creations
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentCreated += OnDocumentCreated;
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;

                // 2. Attach to ALL CURRENTLY open documents
                foreach (Document doc in Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager)
                {
                    if (doc.Database != null)
                    {
                        WallSyncReactor.Instance.Register(doc.Database);
                        WallSyncReactor.Instance.RegisterDocEvents(doc);
                    }
                }
                
                // Global Settings
                Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("PROXYGRAPHICS", 1);
            }
            catch { }
        }

        private void OnDocumentCreated(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document != null && e.Document.Database != null)
            {
                WallSyncReactor.Instance.Register(e.Document.Database);
                WallSyncReactor.Instance.RegisterDocEvents(e.Document);
            }
        }

        private void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document != null && e.Document.Database != null)
            {
                WallSyncReactor.Instance.Unregister(e.Document.Database);
            }
        }

        public void Terminate()
        {
            try
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentCreated -= OnDocumentCreated;
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;

                foreach (Document doc in Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager)
                {
                    if (doc.Database != null)
                    {
                        WallSyncReactor.Instance.Unregister(doc.Database);
                    }
                }
            }
            catch { }
        }
    }
}
