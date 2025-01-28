using UnityEngine;

//Class detects stylus input and triggers events based on input changes. Helps implement custom logic.
public class CustomStylusHandler : MonoBehaviour
{
    // Haptic Feedback Control
    [SerializeField] private float hapticClickDuration = 0.05f;
    [SerializeField] private float hapticClickAmplitude = 0.9f;
    [SerializeField] private float hapticClickMinThreshhold = 0.9f;

    //Stores stylus's state and previous inputs
    private StylusInputs _stylus;

    //Stores stylus input values and state information: ex. is active, L/R hand, current pose
    public StylusInputs Stylus => _stylus;

    //Events triggered based on changes in the stylus input states
    //Allows easier calling of actions from different inputs
    public event System.Action OnFrontPressed;
    public event System.Action OnFrontReleased;
    public event System.Action OnBackPressed;
    public event System.Action OnBackReleased;
    public event System.Action OnDocked;
    public event System.Action OnUndocked;

    //Stores previous values
    private bool _previousFrontValue;
    private bool _previousBackValue;
    private bool _previousDockedValue;


    private const string InkPoseRight = "aim_right";
    private const string InkPoseLeft = "aim_left";
    private const string InkHapticPulse = "haptic_pulse";

    //Updates the stylus's pose and inputs, checking for state changes
    //Generates haptic feedback when necessary
    private void Update()
    {
        OVRInput.Update();
        UpdateStylusPose();
        UpdateStylusInputs();
        CheckBooleanEvents();
        GenerateHapticFeedback();
    }

    //Checks if stylus is active and if so, which hand is holding it by checking the left/right hand interaction profile
    private void UpdateStylusPose()
    {
        //Interaction profile checking for each hand
        var leftDevice = OVRPlugin.GetCurrentInteractionProfileName(OVRPlugin.Hand.HandLeft);
        var rightDevice = OVRPlugin.GetCurrentInteractionProfileName(OVRPlugin.Hand.HandRight);

        //Checks weather or not the stylus is being held
        _stylus.isActive = leftDevice.Contains("logitech") || rightDevice.Contains("logitech");
        _stylus.isOnRightHand = rightDevice.Contains("logitech");

        var poseAction = _stylus.isOnRightHand ? InkPoseRight : InkPoseLeft;

        if (!OVRPlugin.GetActionStatePose(poseAction, out var handPose)) return;
        transform.localPosition = handPose.Position.FromFlippedZVector3f();
        transform.localRotation = handPose.Orientation.FromFlippedZQuatf();
        //Updates stylus position and rotation based on information recieved
        _stylus.inkingPose.position = transform.localPosition;
        _stylus.inkingPose.rotation = transform.localRotation;
    }

    //Updates input values, fetched using helper methods
    private void UpdateStylusInputs()
    {
        //Pressure on tip
        _stylus.tip_value = GetActionStateFloat("tip");
        _stylus.cluster_middle_value = GetActionStateFloat("middle");
        _stylus.cluster_front_value = GetActionStateBoolean("front");
        _stylus.cluster_back_value = GetActionStateBoolean("back");
        _stylus.docked = GetActionStateBoolean("dock");
    }

    //Helper method for fetching action state values (float)
    //Interfaces with OVR Plugin to get current state of actionName
    private float GetActionStateFloat(string actionName)
    {
        return OVRPlugin.GetActionStateFloat(actionName, out var value) ? value : 0f;
    }

    //Helper method for fetching action state values (bool)
    //Interfaces with OVR Plugin to get current state of actionName
    private bool GetActionStateBoolean(string actionName)
    {
        return OVRPlugin.GetActionStateBoolean(actionName, out var value) && value;
    }

    //Compares current + previous values to detect changes, if changed: corresponding event is triggered
    private void CheckBooleanEvents()
    {
        switch (_stylus.cluster_front_value)
        {
            case true when !_previousFrontValue:
                OnFrontPressed?.Invoke();
                break;
            case false when _previousFrontValue:
                OnFrontReleased?.Invoke();
                break;
        }
        _previousFrontValue = _stylus.cluster_front_value;

        switch (_stylus.cluster_back_value)
        {
            case true when !_previousBackValue:
                OnBackPressed?.Invoke();
                break;
            case false when _previousBackValue:
                OnBackReleased?.Invoke();
                break;
        }
        _previousBackValue = _stylus.cluster_back_value;

        switch (_stylus.docked)
        {
            case true when !_previousDockedValue:
                OnDocked?.Invoke();
                break;
            case false when _previousDockedValue:
                OnUndocked?.Invoke();
                break;
        }
        _previousDockedValue = _stylus.docked;

    }

    # region HAPTIC FEEDBACK HANDLING
    //Determines weather haptic feedback should be triggered based on the stylus's input values ex: tip/middle button pressed down with enough force
    private void GenerateHapticFeedback()
    {
        var holdingHand = _stylus.isOnRightHand ? OVRPlugin.Hand.HandRight : OVRPlugin.Hand.HandLeft;
        GenerateHapticClick(_stylus.tip_value, holdingHand);
        GenerateHapticClick(_stylus.cluster_middle_value, holdingHand);
    }

    //If the value of tip or pressure button is above the threshhold, generate haptic feedback via OVRPlugin
    private void GenerateHapticClick(float analogValue, OVRPlugin.Hand hand)
    {
        if (analogValue >= hapticClickMinThreshhold)
        {
            TriggerHapticFeedback(hand);
        }
    }

    //Uses OVRPlugin to trigger haptic feedback in hand holding stylus
    private void TriggerHapticFeedback(OVRPlugin.Hand hand)
    {
        OVRPlugin.TriggerVibrationAction(InkHapticPulse, hand, hapticClickDuration, hapticClickAmplitude);
    }

    # endregion
}
