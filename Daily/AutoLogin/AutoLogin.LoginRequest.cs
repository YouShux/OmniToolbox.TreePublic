using System.Threading.Tasks;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoLogin
{
    private async Task<bool> RequestDataCenterLoginAsync(uint dataCenterID, bool requiresDataCenterLogin)
    {
        if (!requiresDataCenterLogin || IsLobbyTravelReady())
            return true;

        if (!IsDataCenterLoginAvailable())
        {
            NotifyHelper.Instance().NotificationWarning(OmniLoc.Get(this, "Feature.AutoLogin.DataCenterUnavailable"));
            SuspendUntilNextLogin();
            return true;
        }

        return await selectDataCenterAndLogin.InvokeFunc(LuminaWrapper.GetDataCenterName(dataCenterID));
    }
}
