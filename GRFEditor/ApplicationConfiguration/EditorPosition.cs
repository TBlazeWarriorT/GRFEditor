using System.Collections.Generic;
using Utilities;

namespace GRFEditor.ApplicationConfiguration {
	public class EditorPosition {
		public bool IsSet { get; set; }
		public double Width { get; set; }
		public double Height { get; set; }
		public double X { get; set; }
		public double Y { get; set; }
		public double Splitter1 { get; set; }
		public double Splitter2 { get; set; }
		public bool Maximized { get; set; }

		public void FromString(string config) {
			var items = Methods.StringToList(config, ';');
			int i = 0;

			if (items.Count == 0)
				return;

			IsSet = FormatConverters.BooleanConverter(items[i++]);
			Width = FormatConverters.DoubleConverterNoThrow(items[i++]);
			Height = FormatConverters.DoubleConverterNoThrow(items[i++]);
			X = FormatConverters.DoubleConverterNoThrow(items[i++]);
			Y = FormatConverters.DoubleConverterNoThrow(items[i++]);
			Splitter1 = FormatConverters.DoubleConverterNoThrow(items[i++]);
			Splitter2 = FormatConverters.DoubleConverterNoThrow(items[i++]);
			if (i < items.Count)
				Maximized = FormatConverters.BooleanConverter(items[i++]);
		}

		public override string ToString() {
			if (!IsSet)
				return "";

			List<string> values = new List<string>();
			values.Add(IsSet + "");
			values.Add(Width + "");
			values.Add(Height + "");
			values.Add(X + "");
			values.Add(Y + "");
			values.Add(Splitter1 + "");
			values.Add(Splitter2 + "");
			values.Add(Maximized + "");

			return Methods.Aggregate(values, ";");
		}

		public void Load(EditorMainWindow editor) {
			FromString(GrfEditorConfiguration.EditorSavedPositions);

			if (!IsSet || !GrfEditorConfiguration.SaveEditorPosition)
				return;

			if (Width >= 680)
				editor.Width = Width;
			if (Height >= 400)
				editor.Height = Height;
			editor.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
			editor.Left = X;
			editor.Top = Y;
			if (Splitter1 >= 80)
				editor._gridTreeView.ColumnDefinitions[0].Width = new System.Windows.GridLength(Splitter1);
			if (Splitter2 >= 160)
				editor._primaryGrid.ColumnDefinitions[0].Width = new System.Windows.GridLength(Splitter2);
			if (Maximized)
				editor.WindowState = System.Windows.WindowState.Maximized;
		}

		public void Save(EditorMainWindow editor) {
			IsSet = true;
			Maximized = editor.WindowState == System.Windows.WindowState.Maximized;
			if (Maximized) {
				var bounds = editor.RestoreBounds;
				Width = bounds.Width;
				Height = bounds.Height;
				X = bounds.Left;
				Y = bounds.Top;
			}
			else {
				Width = editor.Width;
				Height = editor.Height;
				X = editor.Left;
				Y = editor.Top;
			}
			Splitter1 = editor._gridTreeView.ColumnDefinitions[0].ActualWidth;
			Splitter2 = editor._primaryGrid.ColumnDefinitions[0].ActualWidth;
			Save();
		}

		public void Save() {
			GrfEditorConfiguration.EditorSavedPositions = ToString();
		}
	}
}
