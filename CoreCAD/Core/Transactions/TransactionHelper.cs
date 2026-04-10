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
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Penanganan Document Locking untuk konteks non-modal (AutoCAD 2026+)
            DocumentLock? docLock = null;
            if (Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.IsApplicationContext)
            {
                docLock = doc.LockDocument();
            }

            try
            {
                using (docLock)
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
                        
                        // Error Logging Enhancement: Catch full stack trace and source method
                        string errorContext = $"Method: {action.Method.Name}";
                        Logger.Write(new Exception($"{errorContext} | {ex.Message}", ex));
                        
                        ed.WriteMessage($"\n[coreCAD ERROR] Process aborted in {action.Method.Name}: {ex.Message}");
                    }
                }
            }
            finally
            {
                // DocumentLock disposes automatically if using 'using' block, 
                // but handled here for clarity in long-running transactions.
            }
        }
    }
}
