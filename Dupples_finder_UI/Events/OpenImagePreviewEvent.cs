using Prism.Events;

namespace Dupples_finder_UI.Events;

public class OpenImagePreviewEvent : PubSubEvent<OpenImagePreviewPayload>;

public class OpenImagePreviewPayload
{
    public string AlternatePath { get; set; }
    public string FilePath { get; set; }
}