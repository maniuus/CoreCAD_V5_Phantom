using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Core.Geometry;
using CoreCAD.Core.Services;
using CoreCAD.Commands;
using CoreCAD.Overrules;
using System;

// CRITICAL: Explicit assembly-level hints for AutoCAD command scanner
// Required for reliable loading in AutoCAD 2025 (.NET 8)
[assembly: ExtensionApplication(typeof(CoreCAD.Core.Initializer))]
[assembly: CommandClass(typeof(CoreCAD.Commands.CmdWall))]
[assembly: CommandClass(typeof(CoreCAD.Commands.CmdRebuild))]

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

                // 3. Register OVERRULES
                Overrule.AddOverrule(RXClass.GetClass(typeof(Entity)), WallViewGripOverrule.Instance, true);
                Overrule.Overruling = true;
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

                // Unregister OVERRULES
                Overrule.RemoveOverrule(RXClass.GetClass(typeof(Entity)), WallViewGripOverrule.Instance);
            }
            catch { }
        }
    }
}
