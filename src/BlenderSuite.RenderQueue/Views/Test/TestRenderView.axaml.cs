using Avalonia.Controls;

namespace BlenderSuite.RenderQueue.Views.Test;

public partial class TestRenderView : UserControl
{
	public TestRenderView()
	{
		InitializeComponent();
	}
	
	
	private void TextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
	{
		// scroll to the end
		if (sender is TextBox)
		{
			LogScrollViewer.ScrollToEnd();
		}
	}
} 