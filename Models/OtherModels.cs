namespace GDSViewer.Models
{
    public interface IRenderable
    {
        public async Task Render(GDS gds, List<CheckboxItem> showLayers) { }
    }

    public class CheckboxItem
    {
        public short Id { get; set; }
        public string Label { get; set; }
        public bool IsSelected { get; set; }
    }

    public class ToolBarItem
    {
        public ToolBarItem(string displayText, string imagePath)
        {
            DisplayText = displayText;
            ImagePath = imagePath;
        }

        public string DisplayText { get; set; }
        public string ImagePath { get; set; }
        //public Event Callback { get; set; }
    }
}
