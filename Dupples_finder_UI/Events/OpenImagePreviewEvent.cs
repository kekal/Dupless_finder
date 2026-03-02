using Prism.Events;

namespace Dupples_finder_UI.Events
{
    public class OpenImagePreviewEvent : PubSubEvent<OpenImagePreviewPayload>;

    public class OpenImagePreviewPayload
    {
        public string FilePath { get; set; }
        public string AlternatePath { get; set; }
    }
}
