﻿using System;
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
		private bool _skipEvent = false;

		protected override void Initialize()
		{
			DocumentContext.Saved += Document_Saved;
		}

		private void Document_Saved(object sender, EventArgs e)
		{
			if (_skipEvent)
			{
				_skipEvent = false;
				return;
			}
			MonoDevelop.Ide.Gui.Document doc = IdeApp.Workbench.ActiveDocument;
			if (doc == null || doc.Editor == null)
			{
				return;
			}
			var success = IdeApp.CommandService.DispatchCommand(RemoveAndSortCommandId, MonoDevelop.Components.Commands.CommandSource.Keybinding);
			if (!success)
			{

				Console.WriteLine("SortAndRemoveOnSave: Cannot find or dispatch command {0}", RemoveAndSortCommandId);
			}
			else
			{
				_skipEvent = true;
				doc.Save();
			}
		}
	}
}
