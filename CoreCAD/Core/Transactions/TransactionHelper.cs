using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace CoreCAD.Core.Transactions
{
    /// <summary>
    /// Enforces the "Sacred Transaction" pattern for all AutoCAD modifications.
    /// Ensures Atomic operations with automatic error logging and rollback.
    /// </summary>
    public static class TransactionHelper
    {
        /// <summary>
        /// Executes an action within a safe, atomic transaction.
        /// </summary>
        /// <param name="action">The logic to execute within the transaction.</param>
        /// <param name="successMessage">Optional message to display in the editor on success.</param>
        public static void ExecuteAtomic(Action<Transaction, Database, Editor> action, string? successMessage = null)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
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
                    Logger.Write(ex);
                    ed.WriteMessage($"\n[coreCAD ERROR] Process aborted: {ex.Message}");
                }
            }
        }
    }
}
