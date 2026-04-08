using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CoreCAD.Persistence;
using System;
using System.Collections.Generic;

namespace CoreCAD.App
{
    public static class DatabaseWatcher
    {
        private static List<ObjectId> _clonedObjects = new List<ObjectId>();
        public static bool IsDisabled { get; set; } = false;

        public static void Initialize()
        {
            var docMan = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager;
            docMan.DocumentCreated += (s, e) => RegisterEvents(e.Document);
            
            foreach (Document doc in docMan)
                RegisterEvents(doc);
        }

        private static void RegisterEvents(Document doc)
        {
            if (doc == null || doc.Database == null) return;
            doc.Database.ObjectAppended -= Database_ObjectAppended;
            doc.Database.ObjectAppended += Database_ObjectAppended;
        }

        private static void Database_ObjectAppended(object sender, ObjectEventArgs e)
        {
            if (IsDisabled) return;

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            string cmd = doc.CommandInProgress.ToUpper();
            
            // Masukkan ke antrean jika dalam perintah penggandaan
            if (cmd == "COPY" || cmd == "MIRROR" || cmd == "ARRAY" || cmd == "PASTECLIP") 
            {
                _clonedObjects.Add(e.DBObject.ObjectId);
                
                // Daftarkan event Idle jika belum ada (Safe Double-Reg)
                Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnApplicationIdle;
                Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnApplicationIdle;
            }
        }

        private static void OnApplicationIdle(object? sender, EventArgs e)
        {
            // Lepas event agar tidak loop terus menerus
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnApplicationIdle;

            if (_clonedObjects.Count == 0) return;

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument()) 
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction()) 
                {
                    foreach (var id in _clonedObjects) 
                    {
                        if (id.IsNull || !id.IsValid || id.IsErased) continue;

                        try 
                        {
                            var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                            if (ent != null && XDataHelper.HasIdentity(ent)) 
                            {
                                // Reset identitas agar dianggap objek baru di JSON
                                XDataHelper.ResetIdentityForClone(ent);
                                
                                // Tandai Kuning sebagai indikator 'Need Push'
                                ent.ColorIndex = 2; 
                            }
                        }
                        catch { /* Fail silently to prevent crash */ }
                    }
                    tr.Commit();
                }
            }
            
            _clonedObjects.Clear(); // Kosongkan antrean
            doc.Editor.WriteMessage("\n[CoreCAD] Watchdog: Identitas kloning diproses dengan aman (Marker Kuning).");
        }
    }
}
