namespace OmniToolbox.TreePublic;

internal sealed partial class FloatingInfoOverlay
{
    internal sealed class FloatingInfoGroup(Vector2 anchorPosition, FloatingInfoObject item)
    {
        public Vector2 AnchorPosition { get; private set; } = anchorPosition;

        public List<FloatingInfoObject> Objects { get; } = [item];

        public void Reset(Vector2 anchorPosition, FloatingInfoObject item)
        {
            AnchorPosition = anchorPosition;
            Objects.Clear();
            Objects.Add(item);
        }
    }

}