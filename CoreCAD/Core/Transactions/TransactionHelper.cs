using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace CoreCAD.Core.Transactions
{
    /// <summary>
    /// Enforces the "Sacred Transaction" pattern for all AutoCAD modifications.
    /// Ensures Atomic operations with automatic error logging and rollback.
    /// Hardened for CoreCAD V5 to prevent NullReference and eInvalidContext (leakage) exceptions.
    /// </summary>
    public static class TransactionHelper
    {
        public static void ExecuteAtomic(Action<Transaction, Database, Editor> action, string? successMessage = null)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (db == null || ed == null) return;

            // Pattern: Separate DocumentLock from Transaction block to ensure disposal order.
            DocumentLock? docLock = null;
            if (Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.IsApplicationContext)
            {
                docLock = doc.LockDocument();
            }

            try
            {
                using (docLock)
                {
                    // StartTransaction can throw; if so, docLock will still be disposed by 'using'.
                    using (Transaction tr = doc.TransactionManager.StartTransaction())
                    {
                        try
                        {
                            action(tr, db, ed);
                            tr.Commit();

                            if (!string.IsNullOrEmpty(successMessage))
                            {
                                ed.WriteMessage($"\n[coreCAD] {successMessage}");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            tr.Abort();
                            ed.WriteMessage($"\n[coreCAD ERROR] Process aborted: {ex.Message}");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                // Inner WriteMessage might fail if Editor is invalid, but docLock is handled.
                // We don't swallow completely, but stop the crash.
                try { ed.WriteMessage($"\n[coreCAD CRITICAL] Core instability detected: {ex.Message}"); } catch { }
            }
        }
    }
}
