using Autodesk.AutoCAD.Runtime;
using CoreCAD.Core.Registry;
using CoreCAD.Core.Transactions;

// Register the entry point for the AutoCAD plugin
[assembly: ExtensionApplication(typeof(CoreCAD.Core.Initializer))]

namespace CoreCAD.Core
{
    /// <summary>
    /// Automatic initializer for the CoreCAD plugin.
    /// Handles system-wide setups such as Registry and Reactor initialization.
    /// </summary>
    public class Initializer : IExtensionApplication
    {
        /// <summary>
        /// Called when the plugin is loaded into AutoCAD.
        /// </summary>
        public void Initialize()
        {
            // Execute RegApp registration within a "Sacred Transaction"
            TransactionHelper.ExecuteAtomic((tr, db, ed) =>
            {
                XDataManager.EnsureRegApp(db, tr);
            }, "Plugin Registered successfully.");
        }

        /// <summary>
        /// Called when the plugin is being unloaded (AutoCAD closing).
        /// </summary>
        public void Terminate()
        {
            // Cleanup logic if needed
        }
    }
}
