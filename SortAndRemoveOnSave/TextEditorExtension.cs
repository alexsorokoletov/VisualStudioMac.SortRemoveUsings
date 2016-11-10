using System;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Editor.Extension;

namespace SortAndRemoveOnSave
{
    class SortOnSaveTextEditorExtension : TextEditorExtension, IDisposable
    {
        private const string RemoveAndSortCommandId = "MonoDevelop.CSharp.Refactoring.SortAndRemoveImportsCommand";

        public override void Dispose()
        {
            DocumentContext.Saved -= Document_Saved;
            base.Dispose();
        }

        protected override void Initialize()
        {
            DocumentContext.Saved += Document_Saved;
        }

        private void Document_Saved(object sender, EventArgs e)
        {
            MonoDevelop.Ide.Gui.Document doc = IdeApp.Workbench.ActiveDocument;
            if (doc == null || doc.Editor == null)
            {
                return;
            }
            if (!IdeApp.CommandService.DispatchCommand(RemoveAndSortCommandId, MonoDevelop.Components.Commands.CommandSource.Keybinding))
            {
                Console.WriteLine("SortAndRemoveOnSave: Cannot find or dispatch command {0}", RemoveAndSortCommandId);
            }
        }
    }
}
