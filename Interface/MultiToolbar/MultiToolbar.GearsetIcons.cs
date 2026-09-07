using Lumina.Data.Files;
using OmniToolbox.Host;
using OmniToolbox.UI.Controls;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private UldFile? gearsetCharacterUld;

    private bool DrawGearsetNativeButton(string id, uint partsID, uint partID, string tooltip)
    {
        gearsetCharacterUld ??= DalamudServices.DataManager.GetFile<UldFile>("ui/uld/Character.uld");
        var size = new Vector2(ScaleToolbar(32f));
        var position = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (gearsetCharacterUld is { } uld)
        {
            foreach (var parts in uld.Parts)
            {
                if (parts.Id != partsID || partID >= parts.Parts.Length)
                {
                    continue;
                }

                var part = parts.Parts[partID];
                foreach (var asset in uld.AssetData)
                {
                    if (asset.Id != part.TextureId)
                    {
                        continue;
                    }

                    var path = new string(asset.Path).TrimEnd('\0');
                    var texture = DalamudServices.TextureProvider.GetFromGame(path).GetWrapOrDefault();
                    if (texture is not null)
                    {
                        var textureSize = new Vector2(texture.Width, texture.Height);
                        var brightness = hovered ? 1f : 0.8f;
                        var tint = new Vector4(brightness, brightness, brightness, ImGui.GetStyle().Alpha);
                        ImGui.GetWindowDrawList().AddImage(texture.Handle, position, position + size,
                            new Vector2(part.U, part.V) / textureSize,
                            new Vector2(part.U + part.W, part.V + part.H) / textureSize,
                            ImGui.ColorConvertFloat4ToU32(tint));
                    }

                    break;
                }

                break;
            }
        }

        if (hovered)
        {
            OmniControls.HelpTooltip(tooltip);
        }

        return clicked;
    }
}
