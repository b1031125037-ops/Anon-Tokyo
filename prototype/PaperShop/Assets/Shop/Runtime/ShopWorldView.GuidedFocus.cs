using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopWorldView
    {
        Vector3 guidedFocusStart,guidedFocusTarget;
        float guidedFocusRemaining;
        public string GuidedFacilityId {get;private set;}
        public void FocusFacility(string id)
        {
            if(!facilityVisuals.TryGetValue(id,out var item))return;
            GuidedFacilityId=id;guidedFocusStart=focus;guidedFocusTarget=item.center+Vector3.up*.4f;guidedFocusRemaining=.65f;
        }
        public void TickGuidedFocus(float dt)
        {
            if(guidedFocusRemaining<=0)return;
            guidedFocusRemaining=Mathf.Max(0,guidedFocusRemaining-dt);
            focus=Vector3.Lerp(guidedFocusStart,guidedFocusTarget,Mathf.SmoothStep(0,1,1-guidedFocusRemaining/.65f));ApplyCamera();
        }
    }
}
