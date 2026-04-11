using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CoreCAD.Core.Services;
using CoreCAD.Overrules;
using System;

[assembly: ExtensionApplication(typeof(CoreCAD.Core.Initializer))]

namespace CoreCAD.Core
{
    /// <summary>
    /// File inisialisasi aplikasi saat DLL di-load ke AutoCAD.
    /// Tempat pendaftaran Reactor, Overrule, dan Standard Loader.
    /// </summary>
    public class Initializer : IExtensionApplication
    {
        public void Initialize()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.Editor.WriteMessage("\n[CoreCAD V5] Loading Engine (Master Stable)...");
                doc.Editor.WriteMessage("\n[CoreCAD V5] Standards: A-WALL, A-WALL-HATCH, C-CENT.");
                doc.Editor.WriteMessage("\n[CoreCAD V5] Engine Ready.\n");
            }

            // 1. Subscribe to FUTURE document creations
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentCreated += OnDocumentCreated;
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;

            // 2. Attach to ALL CURRENTLY open documents
            foreach (Document d in Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager)
            {
                if (d.Database != null)
                {
                    WallSyncReactor.Instance.Register(d.Database);
                }
            }

            // [STANDARD] Muat konfigurasi dari drawing_standard.json
            StandardManager.Instance.Load();

            // [OVERRULE] Registrasi WallViewGripOverrule
            // Ini akan menekan tampilan grip (titik biru) pada raga dinding.
            GripOverrule.AddOverrule(RXClass.GetClass(typeof(Entity)), WallViewGripOverrule.Instance, false);
            
            // Aktifkan Overrule secara global
            Autodesk.AutoCAD.Runtime.Overrule.Overruling = true;

            // Global Settings
            Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("PROXYGRAPHICS", 1);
        }

        private void OnDocumentCreated(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document?.Database != null)
            {
                WallSyncReactor.Instance.Register(e.Document.Database);
            }
        }

        private void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document?.Database != null)
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

                foreach (Document d in Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager)
                {
                    if (d.Database != null)
                    {
                        WallSyncReactor.Instance.Unregister(d.Database);
                    }
                }

                // [OVERRULE] Unregister saat unload
                GripOverrule.RemoveOverrule(RXClass.GetClass(typeof(Entity)), WallViewGripOverrule.Instance);
            }
            catch { }
        }
    }
}
