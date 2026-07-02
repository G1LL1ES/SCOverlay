using SCOverlay.Core.Input;
using SCOverlay.Core.Profiles;

namespace SCOverlay.Core.Rendering;

public interface IOverlayStateEngine
{
    OverlayState BuildState(OverlayProfile profile, InputSnapshot inputSnapshot);

    void Reset();
}
